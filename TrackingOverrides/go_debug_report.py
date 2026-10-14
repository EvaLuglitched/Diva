"""Summarise a walking (GO) diagnostics log written by bridge.py (Tracking/go_debug/go_<time>.csv).

    python go_debug_report.py              # newest log next to this script (or in ../DigiPhantStarter/Tracking)
    python go_debug_report.py path/to/go_20261012_150000.csv

Shows how often P1 was judged walking, how often walking switched on/off, and - for frames where the arms were
clearly swinging but GO was off - which rule stopped it (hands too low, swing too small, paused, wrists unclear...).
"""
import csv
import statistics
import sys
from collections import Counter
from pathlib import Path

REASONS_ZH = {
    'go': '在走',
    'warming-up': '刚开始摆，记录还不够 0.25 秒 / 4 帧',
    'hands-too-low': '手太低（低于 go_hand_min_height）',
    'hands-not-up': '1 秒内手没抬到肚脐高度（go_reach），手垂着不算走',
    'swing-too-small': '摆动幅度不够（go_range）',
    'paused': '停顿超过 go_stop_delay，判为停下',
    'hands-above-head': '双手举过头（当作大招姿势）',
    'wrists-unclear-hold': '手腕短暂看不清，保持原状态',
    'wrists-unclear': '手腕看不清超过 0.3 秒，清空记录',
    'frame-gap-reset': '两帧间隔超过 0.3 秒（程序太慢），清空记录',
    'thumbs-up-refill': '正在竖拇指补水',
    'hips-missing': '看不到髋部',
    'shoulders-missing': '看不到肩膀',
    'shoulders-unclear': '肩膀不清楚',
    'body-lost': '人丢了',
    'not-assigned': 'P1 没有被分配（没认出这个人）',
}


def newest_log():
    here = Path(__file__).resolve().parent
    for folder in (here / 'go_debug', here.parent / 'DigiPhantStarter' / 'Tracking' / 'go_debug'):
        logs = sorted(folder.glob('go_*.csv'))
        if logs:
            return logs[-1]
    return None


def summarize(path, swing_threshold=.10):
    rows = list(csv.DictReader(open(path, encoding='utf-8')))
    if not rows:
        return None
    num = lambda r, k: float(r[k]) if r.get(k) not in (None, '') else None
    go = [num(r, 'go') or 0. for r in rows]
    t = [num(r, 't') for r in rows]
    loops = [b - a for a, b in zip(t, t[1:]) if b > a]   # time between camera frames the bridge processed
    segments, start = [], 0
    for i in range(1, len(go) + 1):
        if i == len(go) or (go[i] > 0) != (go[i - 1] > 0):
            segments.append((go[start] > 0, (t[i - 1] - t[start]) if i > start else 0.)); start = i
    on = [d for g, d in segments if g]
    off = [d for g, d in segments if not g]
    # Frames where the arms were clearly swinging (either wrist moved a fair amount) but GO was off.
    swinging_off = [r['reason'] for r in rows if (num(r, 'go') or 0) == 0
                    and max(num(r, 'left_range') or 0, num(r, 'right_range') or 0) > swing_threshold]
    return dict(
        file=str(path), frames=len(rows), seconds=(t[-1] - t[0]) if len(t) > 1 else 0.,
        fps=1 / statistics.median(loops) if loops else 0., max_frame_gap=max(loops) if loops else 0.,
        slow_frames=sum(1 for d in loops if d > .3),
        walking_share=sum(1 for g in go if g > 0) / len(go), switches=len(segments) - 1,
        median_walk=statistics.median(on) if on else 0., median_stop=statistics.median(off) if off else 0.,
        reasons=Counter(r['reason'] for r in rows), swinging_but_stopped=Counter(swinging_off),
        median_wrist_q=statistics.median([num(r, 'wrist_q') for r in rows if num(r, 'wrist_q') is not None] or [0]),
        lowest_hand=min([min(num(r, 'left_h'), num(r, 'right_h')) for r in rows
                         if num(r, 'left_h') is not None and num(r, 'right_h') is not None] or [0]))


def main():
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else newest_log()
    if not path or not path.exists():
        raise SystemExit('No go_debug log found. Play once in Camera mode with the Diva bridge, then run this again.')
    s = summarize(path)
    if not s:
        raise SystemExit(f'{path} is empty.')
    print(f"文件 {s['file']}")
    print(f"时长 {s['seconds']:.0f} 秒，{s['frames']} 帧；追踪程序约 {s['fps']:.0f} 帧/秒，最长两帧间隔 {s['max_frame_gap']:.2f} 秒，"
          f"超过 0.3 秒的 {s['slow_frames']} 次")
    print(f"判定在走的帧 {s['walking_share'] * 100:.0f}%；走/停切换 {s['switches']} 次；每段走 {s['median_walk']:.2f} 秒，"
          f"每段停 {s['median_stop']:.2f} 秒（中位数）")
    print(f"手腕可见度中位数 {s['median_wrist_q']:.2f}；手最低到肩膀下 {abs(s['lowest_hand']):.2f} 个躯干长度")
    print('\n所有帧的原因：')
    for reason, count in s['reasons'].most_common():
        print(f"  {count / s['frames'] * 100:5.1f}%  {reason:20s} {REASONS_ZH.get(reason, '')}")
    total = sum(s['swinging_but_stopped'].values())
    if total:
        print('\n手臂明显在摆、但判定为不走的帧，原因是：')
        for reason, count in s['swinging_but_stopped'].most_common():
            print(f"  {count / total * 100:5.1f}%  {reason:20s} {REASONS_ZH.get(reason, '')}")


if __name__ == '__main__':
    main()
