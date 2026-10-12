"""The bridge's walking diagnostics CSV and go_debug_report.summarize work together."""
import tempfile
import unittest
from pathlib import Path
from bridge import GoLog
from diva_gestures import DivaGestureDetector
from go_debug_report import summarize
from test_go_relaxed import swing
from test_diva_gestures import body


class GoLogTests(unittest.TestCase):
    def test_log_and_summary(self):
        with tempfile.TemporaryDirectory() as folder:
            log = GoLog(Path(folder) / 'go_debug')
            d = DivaGestureDetector()
            for i in range(120):
                b = swing(i) if i < 90 else body(height=0)          # swing 3 s, then stand still
                g = d.update(1, b, i / 30)
                log.write(i / 30, 1, 1, 1, g['go'], d.go_debug.get(1))
            log.write(4.1, 0, 0, 1, 0., None)                          # P1 not recognised
            log.close()
            s = summarize(log.path)
            self.assertEqual(s['frames'], 121)
            self.assertGreater(s['walking_share'], .5)
            self.assertIn('go', s['reasons'])
            self.assertIn('not-assigned', s['reasons'])
            self.assertLessEqual(s['switches'], 3)

    def test_keeps_only_newest_logs(self):
        with tempfile.TemporaryDirectory() as folder:
            target = Path(folder) / 'go_debug'; target.mkdir()
            for k in range(15): (target / f'go_2026010{k:02d}.csv').write_text('x')
            GoLog(target, keep=10).close()
            self.assertEqual(len(list(target.glob('go_*.csv'))), 10)


if __name__ == '__main__':
    unittest.main()
