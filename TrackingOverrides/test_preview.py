import socket
import unittest

try:
    import cv2
    import numpy as np
except ImportError:
    cv2 = np = None

from bridge import encode_preview, encode_preview_hd, preview_datagrams


@unittest.skipIf(cv2 is None, "Install Tracking requirements for JPEG tests")
class PreviewTests(unittest.TestCase):
    def test_jpeg_fits_datagram_and_preserves_landscape_and_portrait(self):
        for height, width in ((720, 1280), (1280, 720), (240, 320)):
            pixels = np.random.default_rng(42).integers(0, 256, (height, width, 3), dtype=np.uint8)
            data = encode_preview(pixels, cv2)
            self.assertIsNotNone(data)
            self.assertLessEqual(len(data), 8000)
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as receiver, socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sender:
                receiver.bind(('127.0.0.1', 0))
                receiver.settimeout(1)
                sender.sendto(data, receiver.getsockname())
                self.assertEqual(receiver.recvfrom(8192)[0], data)
            decoded = cv2.imdecode(np.frombuffer(data, dtype=np.uint8), cv2.IMREAD_COLOR)
            h, w = decoded.shape[:2]
            self.assertLessEqual(w, 320)
            self.assertLessEqual(h, 240)
            self.assertAlmostEqual(w / h, width / height, delta=.01)


    def test_hd_preview_is_sharp_and_reassembles_from_datagrams(self):
        pixels = np.random.default_rng(7).integers(0, 256, (720, 1280, 3), dtype=np.uint8)
        jpeg = encode_preview_hd(pixels, cv2)
        decoded = cv2.imdecode(np.frombuffer(jpeg, dtype=np.uint8), cv2.IMREAD_COLOR)
        self.assertEqual(decoded.shape[:2], (540, 960))
        parts = preview_datagrams(jpeg, 41)
        self.assertGreater(len(parts), 1)
        self.assertTrue(all(len(p) <= 8012 for p in parts))
        # Send them over real loopback UDP (macOS limit 9216 bytes) and put them back together.
        received = {}
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as receiver, socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sender:
            receiver.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 1 << 21)
            receiver.bind(('127.0.0.1', 0)); receiver.settimeout(1)
            for part in reversed(parts):
                sender.sendto(part, receiver.getsockname())
            for _ in parts:
                data = receiver.recvfrom(9216)[0]
                self.assertEqual(data[:4], b'DVP1')
                frame, index, count = __import__('struct').unpack('<IHH', data[4:12])
                self.assertEqual((frame, count), (41, len(parts)))
                received[index] = data[12:]
        self.assertEqual(b''.join(received[i] for i in range(len(parts))), jpeg)

    def test_small_frame_is_not_upscaled(self):
        pixels = np.zeros((240, 320, 3), dtype=np.uint8)
        decoded = cv2.imdecode(np.frombuffer(encode_preview_hd(pixels, cv2), dtype=np.uint8), cv2.IMREAD_COLOR)
        self.assertEqual(decoded.shape[:2], (240, 320))


if __name__ == '__main__':
    unittest.main()
