// PNG writer adapted from ThisIsAkill/Table-Tennis-Linux (MIT); see NOTICE.
#pragma once
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <span>
#include <vector>
namespace tabletennis::photo::image {
struct Image { uint32_t width, height; size_t stride; std::span<const uint8_t> data; };
inline bool Valid(const Image& image) {
  return image.width && image.height && image.width<=8192 && image.height<=8192 &&
    uint64_t(image.width)*image.height<=33554432 && uint64_t(image.stride)>=uint64_t(image.width)*4 &&
    image.stride<=image.data.size()/image.height;
}
inline uint32_t OutputHeight(const Image& image,double aspect) {
  if(!image.width || !image.height) return 0;
  if(!std::isfinite(aspect) || aspect<=16.0/9.0) return image.height;
  return uint32_t(std::clamp(std::lround(image.width/std::min(aspect,4.0)),1l,long(image.height)));
}
// --- PNG (stored deflate blocks: no compression library needed) -------------------------------
inline uint32_t Crc32(const uint8_t* data, size_t size) {
  static const std::array<uint32_t, 256> table = [] {
    std::array<uint32_t, 256> t{};
    for (uint32_t n = 0; n < 256; ++n) {
      uint32_t c = n;
      for (int k = 0; k < 8; ++k) c = (c & 1) ? 0xEDB88320u ^ (c >> 1) : c >> 1;
      t[n] = c;
    }
    return t;
  }();
  uint32_t crc = 0xFFFFFFFFu;
  for (size_t i = 0; i < size; ++i) crc = table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
  return ~crc;
}

inline void PutBe32(std::vector<uint8_t>& out, uint32_t v) {
  for (int i = 3; i >= 0; --i) out.push_back(uint8_t(v >> (8 * i)));
}

inline void Chunk(std::vector<uint8_t>& png, const char* type, const std::vector<uint8_t>& data) {
  PutBe32(png, uint32_t(data.size()));
  const size_t start = png.size();
  png.insert(png.end(), type, type + 4);
  png.insert(png.end(), data.begin(), data.end());
  PutBe32(png, Crc32(png.data() + start, png.size() - start));
}

inline bool WritePng(const std::filesystem::path& path, const Image& image, double aspect = 16.0/9.0) {
  if (!Valid(image)) return false;
  const auto height = OutputHeight(image, aspect);
  std::vector<uint8_t> raw;  // filter byte + RGB per row
  raw.reserve(size_t(height) * (size_t(image.width) * 3 + 1));
  for (uint32_t y = 0; y < height; ++y) {
    raw.push_back(0);
    const double sy = std::clamp((double(y)+.5)*image.height/height-.5, 0.0, double(image.height-1));
    const auto top = uint32_t(sy), bottom = std::min(top+1,image.height-1);
    const auto* a = image.data.data()+size_t(top)*image.stride;
    const auto* b = image.data.data()+size_t(bottom)*image.stride;
    const double weight = sy-top;
    for(uint32_t x=0;x<image.width;++x) for(uint32_t c=0;c<3;++c)
      raw.push_back(uint8_t(std::lround(a[x*4+c]*(1-weight)+b[x*4+c]*weight)));
  }
  std::vector<uint8_t> z = {0x78, 0x01};
  size_t at = 0;
  while (true) {
    const size_t n = std::min<size_t>(65535, raw.size() - at);
    const bool last = at + n >= raw.size();
    z.push_back(last ? 1 : 0);
    z.push_back(uint8_t(n));
    z.push_back(uint8_t(n >> 8));
    z.push_back(uint8_t(~n));
    z.push_back(uint8_t(~n >> 8));
    z.insert(z.end(), raw.begin() + ptrdiff_t(at), raw.begin() + ptrdiff_t(at + n));
    at += n;
    if (last) break;
  }
  uint32_t a = 1, b = 0;  // Adler-32
  for (uint8_t c : raw) {
    a = (a + c) % 65521;
    b = (b + a) % 65521;
  }
  PutBe32(z, (b << 16) | a);
  std::vector<uint8_t> png = {0x89, 'P', 'N', 'G', '\r', '\n', 0x1A, '\n'};
  std::vector<uint8_t> header;
  PutBe32(header, image.width);
  PutBe32(header, height);
  header.insert(header.end(), {8, 2, 0, 0, 0});  // 8-bit RGB
  Chunk(png, "IHDR", header);
  Chunk(png, "IDAT", z);
  Chunk(png, "IEND", {});
  std::error_code ec;
  std::filesystem::create_directories(path.parent_path(), ec);
  std::ofstream file(path, std::ios::binary | std::ios::trunc);
  file.write(reinterpret_cast<const char*>(png.data()), std::streamsize(png.size()));
  file.flush();
  return bool(file);
}


}
