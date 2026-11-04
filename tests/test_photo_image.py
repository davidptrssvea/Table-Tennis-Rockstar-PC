"""Decode the actual exported PNG independently of the C++ writer."""
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
import zlib

ROOT = Path(__file__).resolve().parents[1]


@unittest.skipUnless(os.name == 'nt' and os.environ.get('TABLETENNIS_TEST_NATIVE'), 'Opt-in Windows compiler check')
class PhotoImageTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        sys.path.insert(0, str(ROOT/'project/tools'))
        import setup_build
        _, _, clang, _, env = setup_build.build_tools()
        cls.temp = tempfile.TemporaryDirectory(prefix='Table Tennis PNG ')
        cls.folder = Path(cls.temp.name)
        cls.exe = cls.folder/'photo_image.exe'
        subprocess.run([str(clang), '-std=c++23', '-UNDEBUG', '-O2', '-I'+str(ROOT/'project/src'),
                        str(ROOT/'tests/photo_image_probe.cpp'), '-o', str(cls.exe)], env=env, check=True)

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def decode(self, mode, expected):
        path = self.folder/(mode+'.png')
        subprocess.run([str(self.exe), mode, str(path)], check=True, timeout=30)
        data = path.read_bytes()
        self.assertEqual(data[:8], b'\x89PNG\r\n\x1a\n')
        at, compressed, header = 8, bytearray(), None
        while at < len(data):
            size = struct.unpack_from('>I', data, at)[0]
            kind, body = data[at+4:at+8], data[at+8:at+8+size]
            self.assertEqual(struct.unpack_from('>I', data, at+8+size)[0], zlib.crc32(kind+body))
            if kind == b'IHDR': header = struct.unpack('>IIBBBBB', body)
            if kind == b'IDAT': compressed.extend(body)
            at += size+12
        self.assertEqual(header, (*expected, 8, 2, 0, 0, 0))
        raw = zlib.decompress(compressed)
        self.assertEqual(len(raw), expected[1]*(expected[0]*3+1))
        return raw

    def test_padding_and_rgb_channels(self):
        raw = self.decode('normal', (16, 9))
        for y in range(9):
            row = raw[y*49:(y+1)*49]
            self.assertEqual(row[0], 0)
            self.assertEqual(row[1:], bytes(c for x in range(16) for c in (x*17, y*28, x+y)))

    def test_ultrawide_pixel_proportions(self):
        raw = self.decode('wide', (16, 5))
        self.assertEqual(raw[2], 11)  # Bilinear first-row sample, not cropping the scene.
        self.assertEqual(raw[2*49+2], 112)

    def test_4k_export(self):
        self.decode('4k', (3840, 2160))

    def test_optical_zoom_and_limits(self):
        subprocess.run([str(self.exe), 'zoom', str(self.folder/'unused')], check=True, timeout=10)
