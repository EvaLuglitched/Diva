"""Thumbs-up from MediaPipe's pretrained Gesture Recognizer ('Thumb_Up') counts for refill, with the same hold and
hand-to-body matching as the geometry rule; low scores and other gestures do not."""
import unittest
from diva_gestures import DivaGestureDetector
from test_diva_gestures import body
from test_thumb_prayer import thumb_hand


def model_hand(wrist, gesture='Thumb_Up', score=.8):
    h = thumb_hand(wrist, 'side')            # geometry alone would reject this (thumb sideways)
    h.update(gesture=gesture, gesture_score=score)
    return h


class ThumbModelTests(unittest.TestCase):
    def test_model_thumbs_up_refills_after_hold(self):
        d = DivaGestureDetector(); p = body(height=-.2); h = model_hand(p[15])
        for t in (0, .2, .4, .9):
            self.assertEqual(d.update(3, p, t, [h])['drink'], t >= .4)

    def test_low_score_or_other_gesture_is_ignored(self):
        for gesture, score in (('Thumb_Up', .3), ('Open_Palm', .9), ('Closed_Fist', .9), ('Thumb_Up', float('nan'))):
            d = DivaGestureDetector(); p = body(); h = model_hand(p[15], gesture, score)
            for t in (0, .2, .4, .6):
                self.assertFalse(d.update(3, p, t, [h])['drink'], (gesture, score))

    def test_model_hand_must_belong_to_the_player(self):
        d = DivaGestureDetector(); p = body(); far = model_hand(body(offset=.7)[15])
        for t in (0, .2, .4, .6):
            self.assertFalse(d.update(3, p, t, [far])['drink'])


if __name__ == '__main__':
    unittest.main()
