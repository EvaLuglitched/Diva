"""🔒 LOCKED (2026-10-12): single-player and three-player gesture controls are final. Do not change rules or
thresholds without the double check in GESTURES_LOCKED.md (team + Eva approval, PR, all tests, live test).

Diva gesture rules on pretrained MediaPipe landmarks (no action network training).

Coordinates are UNMIRRORED. Recorded LEFT reaches camera-right; RIGHT reaches
camera-left. Thresholds are in torso/shoulder units and configurable in JSON.
"""
from collections import deque
import json
import math
from numbers import Real
from pathlib import Path

# 2026-10-13 (approved by Eva, GESTURES_LOCKED.md): walking made easier for beginners. One arm swinging go_range
# while the other moves at least go_range_other walks (was: both arms .14); slower, uneven swings, swings down to
# the thighs, fast swings that blur the wrists and pumping through the top of the head all keep walking.
# 2026-10-13 later (Eva): P1 stops on time. Motion is measured against the wrist 0.2 s ago, so landmark jitter no
# longer counts as moving (with per-frame steps it kept walking up to 1 s after the arms stopped); pause 0.25 s.
# 2026-10-13 night (Eva, compared with Vicky's original GO/STOP data): no walking with the hands hanging. A swing must
# bring a hand up to go_reach (navel height) at least once in go_window; drifting low-visibility wrists of a player
# standing with the arms down (three players) walked the elephant. Swing sizes .14 / .08: .10 / .05 were at the
# level of a player holding still (Vicky's STOP clips: .11 / .09), and the original pumping is about 1.2 / 0.9.
DEFAULTS = dict(min_quality=.30, go_window=1.0, go_range=.14, go_range_other=.08, go_reach=-.6,
                go_stop_delay=.25, go_step=.04, go_motion_window=.2, go_hand_min_height=-1.8,
                # Wrists unclear for less than this (blur, frame edge) keep the swing history and the walking state.
                go_dropout=.5,
                # Steering = tilting the shoulders or the head toward one side (no hips needed). Tilt is the slope of
                # the shoulder line or the ear (eye) line relative to the calibrated neutral; the larger one counts.
                steer_tilt_start=.14, steer_tilt_release=.08,
                steer_tilt_full=.40, steer_direction=-1.,
                # Shoulders briefly unclear (occluded by a neighbour) keep the current turn this long, then stop.
                steer_dropout=.3,
                # One arm up to aim starts walking only if the other arm moved go_range_other in the last aim_still_window.
                aim_height=.65, aim_still_window=.5, hand_match_distance=.55,
                finger_angle=145., finger_extension=1.2,
                shoot_min_height=-.55, shoot_max_height=.65,
                shoot_forward_z=.20, shoot_elbow_angle=140.,
                thumb_hold=.4, thumb_max_gap=.6, thumb_angle_min=150.,
                thumb_up_cos=.70, thumb_extension=.5, curled_angle_max=135.,
                # MediaPipe Gesture Recognizer's pretrained 'Thumb_Up' (when the bridge runs it) also counts.
                thumb_model_score=.5,
                # Aim while spraying: both hands pushed toward one side (shooting keeps hands below aim_height).
                shoot_aim_full_offset=.35, shoot_aim_deadzone=.06,
                # Ultimate: both wrists well above the head (torso lengths above the shoulders), held still.
                # Walking stops only once both hands have stayed up ult_walk_hold, so pumping high keeps walking.
                ult_height=.85, ult_hold=.8, ult_walk_hold=.4)

def val(point,name,default=0.):
    # MediaPipe hand landmarks carry visibility/presence = None (only pose landmarks fill them): use the default,
    # or comparisons such as quality(p) < min_quality crash the bridge (and Unity shows "Camera stopped").
    v=point.get(name,default) if isinstance(point,dict) else getattr(point,name,default)
    return default if v is None else v

def xyz(point): return tuple(val(point,k) for k in ('x','y','z'))
def xy(point): return xyz(point)[:2]
def confidence_value(value):
    # MediaPipe hand landmarks omit these optional scores (None). Explicit low
    # scores remain authoritative; malformed scores must never become confidence.
    if value is None:return 1.
    if isinstance(value,bool) or not isinstance(value,Real) or not math.isfinite(value) or not 0<=value<=1:return 0.
    return float(value)

def quality(point): return min(confidence_value(val(point,'visibility',1.)),confidence_value(val(point,'presence',1.)))
def scale_body(points,aspect):
    """Convert x/z into image-height units so portrait/landscape agree."""
    return [dict(x=val(p,'x')*aspect,y=val(p,'y'),z=val(p,'z')*aspect,
                 visibility=confidence_value(val(p,'visibility',1.)),presence=confidence_value(val(p,'presence',1.))) for p in points]
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

def thumbs_up(points,config):
    """Image-up extended thumb and four positively folded fingers, not just 'not open'."""
    if len(points)!=21 or any(not all(math.isfinite(c) for c in xyz(p)) or quality(p)<config['min_quality'] for p in points):return False
    palm=max(.015,math.dist(xyz(points[0]),xyz(points[9])))
    delta=tuple(a-b for a,b in zip(xyz(points[4]),xyz(points[2])))
    length=math.sqrt(sum(c*c for c in delta));up=-delta[1]
    if (length<1e-6 or up<config['thumb_extension']*palm or up/length<config['thumb_up_cos']
            or angle(points[2],points[3],points[4])<config['thumb_angle_min']):return False
    return all(math.dist(xyz(points[0]),xyz(points[base]))>.35*palm
               and math.dist(xyz(points[base]),xyz(points[base+1]))>.15*palm
               and math.dist(xyz(points[base+1]),xyz(points[base+3]))>.10*palm
               and angle(points[base],points[base+1],points[base+3])<=config['curled_angle_max']
               and math.dist(xyz(points[0]),xyz(points[base+3]))<=1.1*math.dist(xyz(points[0]),xyz(points[base+1]))
               for base in (5,9,13,17))

def model_thumbs_up(hand,config):
    """The pretrained MediaPipe Gesture Recognizer says this hand is a thumbs-up (bridge adds gesture/gesture_score)."""
    if not isinstance(hand,dict) or hand.get('gesture')!='Thumb_Up':return False
    score=hand.get('gesture_score')
    return isinstance(score,(int,float)) and math.isfinite(score) and score>=config['thumb_model_score']

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
            if len(b)<17 or any(not all(math.isfinite(c) for c in xyz(b[i])) or quality(b[i])<cfg['min_quality'] for i in (11,12)):continue
            scale=max(.03,math.dist(xy(b[11]),xy(b[12])))
            for wrist in (15,16):
                if quality(b[wrist])>=cfg['min_quality'] and all(math.isfinite(c) for c in xyz(b[wrist])):
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
        self.steering={}; self.refill={}; self.ult_since={}
        self.hand_lost={}; self.last_go={}
        # Per slot: why the last frame was GO or not, with the numbers behind it (the bridge logs this).
        self.go_debug={}
    def _held_steer(self,slot,time):
        previous=self.steering.get(slot)
        if previous is not None and 0<=time-previous[1]<=self.config['steer_dropout']:return previous[0]
        return None
    def _why(self,slot,reason,**values):
        self.go_debug[slot]=dict(reason=reason,**values)
    def calibrate(self):
        """Capture the last valid upright stance; camera must be unmirrored."""
        # A missing torso cannot establish a new lean baseline. Keep that slot's
        # previous valid lean while allowing independently valid hand geometry.
        self.neutral={slot:{**self.neutral.get(slot,{}),**g} for slot,g in self.latest_geometry.items()}
        self.history.clear();self.last_motion.clear();self.steering.clear();self.ult_since.clear();self.refill.clear()
        self.hand_lost.clear();self.last_go.clear()
    def reset(self,slot=None,preserve_neutral=False):
        if slot is None:
            self.history.clear();self.last_motion.clear();self.neutral.clear();self.latest_geometry.clear()
            self.steering.clear();self.refill.clear();self.ult_since.clear();self.hand_lost.clear();self.last_go.clear()
        else:
            self.history.pop(slot,None);self.last_motion.pop(slot,None);self.steering.pop(slot,None);self.ult_since.pop(slot,None)
            self.refill.pop(slot,None);self.hand_lost.pop(slot,None);self.last_go.pop(slot,None)
            if not preserve_neutral:self.neutral.pop(slot,None)
            self.latest_geometry.pop(slot,None)
    def retain(self,slots):
        for slot in set(self.history) | set(self.steering) | set(self.latest_geometry) | set(self.refill):
            if slot not in slots:self.reset(slot,preserve_neutral=True)
    def update(self,slot,body,time,hands=None,aspect=1.,allow_estimated_mouth=False):
        out=dict(go=0.,steer=0.,aim=0.,shoot=False,drink=False,ult=False,confidence=0.)
        cfg=self.config
        if body is None or len(body)<25:
            self.reset(slot,preserve_neutral=True);self._why(slot,'body-lost');return out
        b=scale_body(body,aspect)
        required=[11,12]
        shoulders_ok=all(all(math.isfinite(c) for c in xyz(b[i])) for i in required)
        q=min(quality(b[i]) for i in [11,12]) if shoulders_ok else 0.
        if not shoulders_ok or q<cfg['min_quality']:
            held=self._held_steer(slot,time)
            if held is not None:
                # Brief occlusion: keep turning for up to steer_dropout instead of dropping to straight at once.
                out['steer']=held;out['confidence']=cfg['min_quality']
                self._why(slot,'shoulders-unclear-hold',shoulder_q=round(q,2));return out
            self.reset(slot,preserve_neutral=True)
            self._why(slot,'shoulders-unclear' if shoulders_ok else 'shoulders-missing',shoulder_q=round(q,2));return out
        out['confidence']=q
        shoulder=midpoint(b[11],b[12])
        width=max(.03,math.dist(xy(b[11]),xy(b[12])))
        # Either matched hand can refill; the other arm or hips need not be visible.
        thumb_quality=0.
        for hand in hands or []:
            points=scale_body(hand['landmarks'],aspect)
            if len(points)!=21 or not all(math.isfinite(c) for c in xyz(points[0])):continue
            if not (model_thumbs_up(hand,cfg) or thumbs_up(points,cfg)):continue
            matches=sorted((math.dist(xy(points[0]),xy(b[i]))/width,i) for i in (15,16)
                           if quality(b[i])>=cfg['min_quality'] and all(math.isfinite(c) for c in xyz(b[i])))
            if (not matches or matches[0][0]>cfg['hand_match_distance']
                    or len(matches)>1 and matches[1][0]-matches[0][0]<.06):continue
            thumb_quality=max(thumb_quality,min(q,quality(b[matches[0][1]]),min(quality(p) for p in points)))
        if thumb_quality>=cfg['min_quality'] and math.isfinite(time):
            previous=self.refill.get(slot)
            if previous is None or time<=previous[1] or time-previous[1]>cfg['thumb_max_gap']:previous=(time,time)
            self.refill[slot]=(previous[0],time)
            out['confidence']=thumb_quality
            out['drink']=time-previous[0]>=cfg['thumb_hold']
            self.history.pop(slot,None);self.last_motion.pop(slot,None);self.ult_since.pop(slot,None);self.steering.pop(slot,None)
            self.latest_geometry.pop(slot,None)
            self._why(slot,'thumbs-up-refill')
            return out
        self.refill.pop(slot,None)
        # Hips are optional (in a row of three they are often hidden): they only set the torso length for arm heights.
        hips_ok=all(all(math.isfinite(c) for c in xyz(b[i])) for i in (23,24))
        torso_quality=min(quality(b[i]) for i in [23,24]) if hips_ok else 0.
        torso=(max(.03,math.dist(shoulder[:2],midpoint(b[23],b[24])[:2]))
               if hips_ok and torso_quality>=cfg['min_quality'] else width*1.7)
        # Steering: tilt of the shoulder line or the head (ears, else eyes). Positive = toward the performer's own
        # left (their left shoulder / ear lower in the image). Only shoulders and head are used, never hips.
        rolls=[(val(b[11],'y')-val(b[12],'y'))/width]
        for a,c in ((7,8),(2,5)):
            if all(quality(b[i])>=cfg['min_quality'] and all(math.isfinite(v) for v in xyz(b[i])) for i in (a,c)):
                span=math.dist(xy(b[a]),xy(b[c]))
                if span>.25*width:
                    rolls.append((val(b[a],'y')-val(b[c],'y'))/span);break
        self.latest_geometry[slot]={'tilt':rolls}
        base=self.neutral.get(slot,{}).get('tilt',[])
        tilts=[r-(base[k] if k<len(base) else 0.) for k,r in enumerate(rolls)]
        tilt=max(tilts,key=abs)
        previous=self.steering.get(slot)
        same_side=(previous is not None and 0 < time-previous[1] <= .3
                   and previous[0]*cfg['steer_direction']*tilt>0)
        threshold=cfg['steer_tilt_release'] if same_side else cfg['steer_tilt_start']
        if abs(tilt)>threshold:
            out['steer']=cfg['steer_direction']*max(-1.,min(1.,tilt/cfg['steer_tilt_full']))
            self.steering[slot]=(out['steer'],time)
        else:
            self.steering.pop(slot,None)  # Upright stops immediately; no stale turn hold.
        # Steering only needs shoulders (+ head). Invalid arms must not emit other actions.
        hand_valid=(all(all(math.isfinite(c) for c in xyz(b[i])) for i in (13,14,15,16))
                    and min(quality(b[i]) for i in (15,16))>=cfg['min_quality'])
        wrist_q=min(quality(b[i]) for i in (15,16))
        if not hand_valid:
            out['confidence']=min(q,torso_quality)
            since=self.hand_lost.setdefault(slot,time)
            if time-since<cfg['go_dropout'] and slot in self.history:
                # Brief blur / frame edge: keep the swing history and the walking state instead of starting over.
                out['go']=self.last_go.get(slot,0.)
                self._why(slot,'wrists-unclear-hold',wrist_q=round(wrist_q,2),lost_for=round(time-since,2))
                return out
            self.history.pop(slot,None);self.last_motion.pop(slot,None);self.ult_since.pop(slot,None);self.refill.pop(slot,None)
            self.last_go.pop(slot,None)
            self._why(slot,'wrists-unclear',wrist_q=round(wrist_q,2),lost_for=round(time-since,2))
            return out
        self.hand_lost.pop(slot,None)
        out['confidence']=min(q,quality(b[15]),quality(b[16]))
        heights=[(shoulder[1]-val(b[i],'y'))/torso for i in (15,16)]
        normalized=[(shoulder[1]-val(b[i],'y'))/width for i in (15,16)]
        history=self.history.setdefault(slot,deque())
        frame_gap=time-history[-1][0] if history else 0.
        # Carl: a camera hiccup (group detection) under 0.6 s must not drop a pump in progress.
        gap_reset=bool(history) and (time<=history[-1][0] or frame_gap>.6)
        if gap_reset:
            self.reset(slot,preserve_neutral=True);history=self.history.setdefault(slot,deque())
        if history and min(heights)>cfg['go_hand_min_height']:
            # go_step is motion over 100 ms, normalized by the time span. It is measured against the sample about
            # go_motion_window ago (not the previous frame), so landmark jitter, which does not add up, never counts
            # as motion: standing still is seen sooner and walking stops on time.
            ref=next((h for h in reversed(history) if time-h[0]>=cfg['go_motion_window']),history[0])
            motion_step=cfg['go_step']*max(.01,time-ref[0])/.1
            if max(abs(normalized[i]-ref[1][i]) for i in (0,1))>motion_step:
                self.last_motion[slot]=time
        history.append((time,normalized,max(heights)))
        while history and time-history[0][0]>cfg['go_window']:history.popleft()
        ranges=[max(r[1][i] for r in history)-min(r[1][i] for r in history) for i in (0,1)]
        reach=max(r[2] for r in history)   # highest wrist in the window (torso lengths above the shoulders)
        since_motion=time-self.last_motion.get(slot,-999)
        if (len(history)>=4 and time-history[0][0]>=.25 and min(heights)>cfg['go_hand_min_height'] and reach>cfg['go_reach']
                and max(ranges)>cfg['go_range'] and min(ranges)>cfg['go_range_other'] and since_motion<cfg['go_stop_delay']):
            out['go']=1.
        # Why GO is on or off this frame (first failing condition), for the bridge's debug log.
        reason=('go' if out['go'] else 'frame-gap-reset' if gap_reset
                else 'warming-up' if len(history)<4 or time-history[0][0]<.25
                else 'hands-too-low' if min(heights)<=cfg['go_hand_min_height']
                else 'hands-not-up' if reach<=cfg['go_reach']
                else 'swing-too-small' if max(ranges)<=cfg['go_range'] or min(ranges)<=cfg['go_range_other']
                else 'paused' )
        go_numbers=dict(left_h=round(heights[0],2),right_h=round(heights[1],2),left_range=round(ranges[0],3),
                        right_range=round(ranges[1],3),since_motion=round(min(since_motion,9.99),2),
                        frame_gap=round(frame_gap,3),wrist_q=round(wrist_q,2))
        wrists=midpoint(b[15],b[16])
        hand_offset=(wrists[0]-shoulder[0])/torso
        geometry=self.latest_geometry.setdefault(slot,{})   # a frame-gap reset above may have cleared it
        geometry['tilt']=rolls; geometry['hand_offset']=hand_offset
        neutral=self.neutral.get(slot,{})
        hand_offset-=neutral.get('hand_offset',0.)
        raised=[h>cfg['aim_height'] for h in heights]
        if raised[0]!=raised[1]:out['aim']=-1. if raised[0] else 1.
        # 2026-10-13 (approved by Eva, v1.6): one arm up to aim never STARTS walking while the other arm is still. Within
        # go_window of a stop the still arm's old swing was still in the history, so lifting the other arm to aim walked
        # the elephant another ~0.45 s. Walking that is already on is unchanged (uneven and high swings keep walking).
        if out['go'] and out['aim'] and not self.last_go.get(slot,0.):
            low=1 if raised[0] else 0
            recent=[r[1][low] for r in history if time-r[0]<=cfg['aim_still_window']]
            if max(recent)-min(recent)<=cfg['go_range_other']:
                out['go']=0.;reason='aiming'
        # Ultimate: both hands above the head and held there. Pumping (go) passes through this height
        # only briefly, so the hold time keeps them apart: walking stops once the hands stay up ult_walk_hold.
        if min(heights)>cfg['ult_height']:
            since=self.ult_since.setdefault(slot,time)
            out['ult']=time-since>=cfg['ult_hold']
            if time-since>=cfg['ult_walk_hold']:
                if out['go']:reason='hands-above-head'
                out['go']=0.
        else:
            self.ult_since.pop(slot,None)
        self.last_go[slot]=out['go']
        self._why(slot,reason,**go_numbers)
        if not hands or len(hands)!=2:return out
        if any(len(h['landmarks'])!=21 or any(not all(math.isfinite(c) for c in xyz(p)) for p in h['landmarks']) for h in hands):return out
        counts=[open_fingers(scale_body(h['landmarks'],aspect),cfg) for h in hands]
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
