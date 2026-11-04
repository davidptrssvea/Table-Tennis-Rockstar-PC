#include "photo_image.h"
#include <cassert>
#include <limits>
#include <string>
int main(int argc,char** argv) {
  assert(argc==3);
  const bool large=std::string(argv[1])=="4k";
  const uint32_t width=large?3840:16,height=large?2160:9;
  const size_t stride=size_t(width)*4+16;
  std::vector<uint8_t> pixels(stride*height,239);
  for(uint32_t y=0;y<height;y++) for(uint32_t x=0;x<width;x++) {
    auto* p=pixels.data()+y*stride+x*4;
    p[0]=uint8_t(x*17);p[1]=uint8_t(y*28);p[2]=uint8_t(x+y);p[3]=255;
  }
  using namespace tabletennis::photo::image;
  Image image{width,height,stride,pixels};
  assert(Valid(image));
  assert(!Valid({0,height,stride,pixels}));
  assert(!Valid({width,height,width*4-1,pixels}));
  assert(!Valid({width,height,stride,std::span(pixels).first(pixels.size()-1)}));
  assert(!Valid({8193,1,stride,pixels}));
  assert(!Valid({width,height,std::numeric_limits<size_t>::max()/height+1,pixels}));
  assert(OutputHeight(image,std::numeric_limits<double>::quiet_NaN())==height);
  assert(WritePng(argv[2],image,std::string(argv[1])=="wide"?32.0/9.0:16.0/9.0));
}
