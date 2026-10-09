"""Diva gesture rules on pretrained MediaPipe landmarks (no action network training).

Coordinates are UNMIRRORED. Recorded LEFT reaches camera-right; RIGHT reaches
camera-left. Thresholds are in torso/shoulder units and configurable in JSON.
"""
from collections import deque
import json
import math
from pathlib import Path

DEFAULTS = dict(min_quality=.30, go_window=.65, go_range=.16,
                go_stop_delay=.28, go_step=.045, go_hand_min_height=-.85,
                steer_hand_offset=.12, steer_joint_distance=.80,
                steer_full_offset=.45, steer_direction=-1.,
                aim_height=.65, hand_match_distance=.55,
                finger_angle=145., finger_extension=1.2,
                shoot_min_height=-.55, shoot_max_height=.65,
                shoot_forward_z=.20, shoot_elbow_angle=140.,
                drink_min_height=-.03, drink_max_height=.65,
                drink_separation=1.45, drink_mouth_distance=.38,
                drink_below_mouth=.22, steer_opposing_lean=.12,
                # Steering hysteresis: once turning, keep turning while the hands stay offset by the
                # smaller release distance, and ride through tracking blips up to steer_hold seconds.
                steer_release_offset=.07, steer_hold=.25,
                # Aim while spraying: both hands pushed toward one side (shooting keeps hands below aim_height).
                shoot_aim_full_offset=.35, shoot_aim_deadzone=.06,
                # Hands in front of the face hide the mouth landmarks; reuse its last seen place for a while.
                mouth_memory=3.)

def val(point,name,default=0.):
    return point.get(name,default) if isinstance(point,dict) else getattr(point,name,default)

def xyz(point): return tuple(val(point,k) for k in ('x','y','z'))
def xy(point): return xyz(point)[:2]
def quality(point): return min(val(point,'visibility',1.),val(point,'presence',1.))
def scale_body(points,aspect):
    """Convert x/z into image-height units so portrait/landscape agree."""
    return [dict(x=val(p,'x')*aspect,y=val(p,'y'),z=val(p,'z')*aspect,
                 visibility=val(p,'visibility',1.),presence=val(p,'presence',1.)) for p in points]
def midpoint(a,b):return tuple((x+y)/2 for x,y in zip(xyz(a),xyz(b)))
def angle(a,b,c):
    u=[x-y for x,y in zip(xyz(a),xyz(b))]; v=[x-y for x,y in zip(xyz(c),xyz(b))]
    den=math.sqrt(sum(x*x for x in u)*sum(x*x for x in v))
    return math.degrees(math.acos(max(-1.,min(1.,sum(x*y for x,y in zip(u,v))/den)))) if den>1e-9 else 0.

def open_fingers(landmarks,config=None):
    cfg=config or DEFAULTS
    if len(landmarks)!=21 or any(not all(math.isfinite(c) for c in xyz(p)) for p in landmarks):return 0
    opened=0
    for base in (5,9,13,17):
        if (angle(landmarks[base],landmarks[base+1],landmarks[base+3])>=cfg['finger_angle']
                and math.dist(xyz(landmarks[0]),xyz(landmarks[base+3])) >
                cfg['finger_extension']*math.dist(xyz(landmarks[0]),xyz(landmarks[base]))):
            opened+=1
    return opened

def associate_hands(hands,bodies,config=None,aspect=1.):
    """Match each hand to its nearest visible BODY wrist; reject close ties.

    The map includes all bodies, preventing a P1/P2 hand from controlling P3.
    One detection per wrist; ambiguous crossings stop hand actions.
    """
    cfg=config or DEFAULTS; output={slot:[] for slot in bodies}; candidates=[]
    for index,h in enumerate(hands):
        points=scale_body(h['landmarks'],aspect)
        if len(points)!=21 or any(not all(math.isfinite(c) for c in xyz(p)) for p in points):continue
        matches=[]
        for slot,raw_b in bodies.items():
            b=scale_body(raw_b,aspect)
            scale=max(.03,math.dist(xy(b[11]),xy(b[12])))
            for wrist in (15,16):
                if quality(b[wrist])>=cfg['min_quality']:
                    matches.append((math.dist(xy(points[0]),xy(b[wrist]))/scale,slot,wrist))
        matches.sort()
        if not matches or matches[0][0]>cfg['hand_match_distance']:continue
        if len(matches)>1 and matches[1][0]-matches[0][0]<.06:continue
        distance,slot,wrist=matches[0]; candidates.append((distance,index,slot,wrist))
    used=set()
    for _,index,slot,wrist in sorted(candidates):
        if (slot,wrist) not in used:
            output[slot].append(hands[index]);used.add((slot,wrist))
    return output

class DivaGestureDetector:
    def __init__(self,config_path=None):
        self.config=DEFAULTS.copy()
        if config_path and Path(config_path).exists():
            self.config.update(json.loads(Path(config_path).read_text(encoding='utf-8')))
        self.history={}; self.last_motion={}; self.neutral={}; self.latest_geometry={}
        self.steering={}; self.mouth={}
    def calibrate(self):
        """Capture the last valid upright stance; camera must be unmirrored."""
        self.neutral={slot:dict(g) for slot,g in self.latest_geometry.items()}
        self.history.clear();self.last_motion.clear();self.steering.clear()
    def reset(self,slot=None,preserve_neutral=False):
        if slot is None:
            self.history.clear();self.last_motion.clear();self.neutral.clear();self.latest_geometry.clear()
            self.steering.clear();self.mouth.clear()
        else:
            self.history.pop(slot,None);self.last_motion.pop(slot,None);self.steering.pop(slot,None)
            if not preserve_neutral:self.neutral.pop(slot,None);self.mouth.pop(slot,None)
            self.latest_geometry.pop(slot,None)
    def retain(self,slots):
        for slot in list(self.history):
            if slot not in slots:self.reset(slot,preserve_neutral=True)
    def update(self,slot,body,time,hands=None,aspect=1.,allow_estimated_mouth=False):
        out=dict(go=0.,steer=0.,aim=0.,shoot=False,drink=False,confidence=0.)
        cfg=self.config
        if body is None or len(body)<25:
            self.reset(slot,preserve_neutral=True);return out
        b=scale_body(body,aspect)
        required=[11,12,13,14,15,16,23,24]
        if any(not all(math.isfinite(c) for c in xyz(b[i])) for i in required):
            self.reset(slot,preserve_neutral=True);return out
        q=min(quality(b[i]) for i in [11,12,15,16])
        if q<cfg['min_quality']:
            self.reset(slot,preserve_neutral=True);return out
        out['confidence']=q
        shoulder=midpoint(b[11],b[12]); hip=midpoint(b[23],b[24])
        width=max(.03,math.dist(xy(b[11]),xy(b[12])))
        torso_quality=min(quality(b[i]) for i in [23,24])
        torso=max(.03,math.dist(shoulder[:2],hip[:2])) if torso_quality>=cfg['min_quality'] else width*1.7
        heights=[(shoulder[1]-val(b[i],'y'))/torso for i in (15,16)]
        normalized=[(shoulder[1]-val(b[i],'y'))/width for i in (15,16)]
        history=self.history.setdefault(slot,deque())
        if history and (time<=history[-1][0] or time-history[-1][0]>.3):
            self.reset(slot,preserve_neutral=True);history=self.history.setdefault(slot,deque())
        if history and min(heights)>cfg['go_hand_min_height']:
            # go_step is motion over 100 ms, normalized by frame time. A 30 fps
            # live camera must not need the larger jumps of a 10 fps dataset.
            motion_step=cfg['go_step']*max(.01,time-history[-1][0])/.1
            if max(abs(normalized[i]-history[-1][1][i]) for i in (0,1))>motion_step:
                self.last_motion[slot]=time
        history.append((time,normalized))
        while history and time-history[0][0]>cfg['go_window']:history.popleft()
        ranges=[max(r[1][i] for r in history)-min(r[1][i] for r in history) for i in (0,1)]
        if (len(history)>=4 and time-history[0][0]>=.25 and min(heights)>cfg['go_hand_min_height']
                and min(ranges)>cfg['go_range'] and time-self.last_motion.get(slot,-999)<cfg['go_stop_delay']):
            out['go']=1.
        wrists=midpoint(b[15],b[16])
        hand_offset=(wrists[0]-shoulder[0])/torso
        sep=math.dist(xy(b[15]),xy(b[16]))/torso
        lean=(shoulder[0]-hip[0])/torso
        self.latest_geometry[slot]=dict(hand_offset=hand_offset,lean=lean)
        neutral=self.neutral.get(slot,{})
        hand_offset-=neutral.get('hand_offset',0.)
        lean-=neutral.get('lean',0.)
        steer_pose=(torso_quality>=cfg['min_quality'] and sep<cfg['steer_joint_distance'] and max(heights)<.4
                    and not(abs(lean)>cfg['steer_opposing_lean'] and lean*hand_offset<0))
        steer_value=cfg['steer_direction']*max(-1.,min(1.,hand_offset/cfg['steer_full_offset']))
        previous=self.steering.get(slot)  # (steer value, time it was last produced)
        same_side=previous is not None and previous[0]*steer_value>0
        if steer_pose and abs(hand_offset)>cfg['steer_hand_offset'] and abs(lean+hand_offset*.5)>.08:
            out['steer']=steer_value                                     # start (or keep) turning
        elif steer_pose and same_side and abs(hand_offset)>cfg['steer_release_offset']:
            out['steer']=steer_value                                     # hysteresis: still leaning that way
        elif previous is not None and not steer_pose and time-previous[1]<=cfg['steer_hold']:
            out['steer']=previous[0]                                     # brief tracking blip: hold the turn
        if out['steer']!=0 and (previous is None or out['steer']!=previous[0] or steer_pose):
            self.steering[slot]=(out['steer'],time)
        elif out['steer']==0:
            self.steering.pop(slot,None)
        raised=[h>cfg['aim_height'] for h in heights]
        if raised[0]!=raised[1]:out['aim']=-1. if raised[0] else 1.
        # Face landmarks prevent closed fists held at chest/shoulder height from
        # becoming a drink gesture. Legacy recorded samples lack face joints,
        # so their replay uses an estimated mouth (flagged in evaluation notes).
        mouth=None
        if min(quality(b[i]) for i in (9,10))>=.5 and all(math.isfinite(c) for i in (9,10) for c in xyz(b[i])):
            mouth=midpoint(b[9],b[10])
        elif quality(b[0])>=.5 and all(math.isfinite(c) for c in xyz(b[0])):
            mouth=(val(b[0],'x'),val(b[0],'y')+.15*torso,val(b[0],'z'))
        if mouth is not None:
            self.mouth[slot]=((mouth[0]-shoulder[0])/torso,(mouth[1]-shoulder[1])/torso,time)
        elif slot in self.mouth and time-self.mouth[slot][2]<=cfg['mouth_memory']:
            # Hands in front of the face hide it: use where the mouth was, relative to the shoulders.
            ox,oy,_=self.mouth[slot]; mouth=(shoulder[0]+ox*torso,shoulder[1]+oy*torso,shoulder[2])
        elif allow_estimated_mouth:
            mouth=(shoulder[0],shoulder[1]-.30*torso,shoulder[2])
        if not hands or len(hands)!=2:return out
        if any(len(h['landmarks'])!=21 or any(not all(math.isfinite(c) for c in xyz(p)) for p in h['landmarks']) for h in hands):return out
        counts=[open_fingers(scale_body(h['landmarks'],aspect),cfg) for h in hands]
        near_face=(min(heights)>=cfg['drink_min_height'] and max(heights)<=cfg['drink_max_height']
                   and sep<cfg['drink_separation'] and abs(hand_offset)<.65)
        hand_centers=[]
        for hand in hands:
            p=scale_body(hand['landmarks'],aspect)
            hand_centers.append(tuple(sum(xyz(p[i])[axis] for i in (0,5,9,13,17))/5 for axis in range(3)))
        hands_center=tuple(sum(h[axis] for h in hand_centers)/2 for axis in range(3))
        near_face=near_face and mouth is not None and math.dist(hands_center[:2],mouth[:2])/torso<cfg['drink_mouth_distance'] and (
            hands_center[1]-mouth[1])/torso<cfg['drink_below_mouth']
        if near_face and max(counts)<=2:
            out['drink']=True
        else:
            extended=[]
            for shoulder_i,elbow_i,wrist_i in [(11,13,15),(12,14,16)]:
                forward=(val(b[shoulder_i],'z')-val(b[wrist_i],'z'))/torso
                extended.append(quality(b[elbow_i])>=cfg['min_quality'] and (forward>cfg['shoot_forward_z'] or angle(b[shoulder_i],b[elbow_i],b[wrist_i])>cfg['shoot_elbow_angle']))
            out['shoot']=(min(counts)>=3 and all(extended)
                          and min(heights)>=cfg['shoot_min_height'] and max(heights)<=cfg['shoot_max_height'])
            # Raising one arm (aim) and spraying exclude each other, so while spraying the player aims
            # by pushing both hands toward one side. Same sign convention as steering (negative = left).
            if out['shoot'] and out['aim']==0 and abs(hand_offset)>cfg['shoot_aim_deadzone']:
                out['aim']=cfg['steer_direction']*max(-1.,min(1.,hand_offset/cfg['shoot_aim_full_offset']))
        return out
