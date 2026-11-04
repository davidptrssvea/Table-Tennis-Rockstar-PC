#pragma once
#include <cstddef>
#include <cstdint>
namespace tabletennis::prompts::texture {
inline uint32_t BlockOffset(uint32_t x,uint32_t y,uint32_t pitch_blocks) {
  constexpr uint32_t log=4;
  const uint32_t pitch=(pitch_blocks+31)&~31u;
  const uint32_t macro=((x>>5)+(y>>5)*(pitch>>5))<<(log+7);
  const uint32_t micro=((x&7)+((y&0xE)<<2))<<log;
  const uint32_t offset=macro+((micro&~0xFu)<<1)+(micro&0xF)+((y&1)<<4);
  return ((offset&~0x1FFu)<<3)+((y&16)<<7)+((offset&0x1C0)<<2)+
    (((((y&8)>>2)+(x>>3))&3)<<6)+(offset&0x3F);
}
inline bool Fits(uint32_t pitch,uint32_t width,uint32_t height,size_t size) {
  if(!width || !height || width%4 || height%4 || pitch<width/4) return false;
  for(uint32_t y=0;y<height/4;++y) for(uint32_t x=0;x<width/4;++x)
    if(uint64_t(BlockOffset(x,y,pitch))+16>size) return false;
  return true;
}
struct Signature { uint32_t width,height; uint64_t xbox,playstation; };
// Several original icons share one PS replacement. A known placement must retain
// its exact original identity across re-uploads, or switching back restores the
// wrong artwork. An unknown PS image alone cannot identify its original variant.
template<class Icons>
int Identify(const Icons& icons,uint32_t width,uint32_t height,uint64_t hash,int known=-1) {
  if(known>=0 && size_t(known)<icons.size()) {
    const auto& i=icons[size_t(known)];
    if(i.width==width && i.height==height && (hash==i.xbox || hash==i.playstation)) return known;
  }
  for(size_t n=0;n<icons.size();++n) {
    const auto& i=icons[n];
    if(i.width==width && i.height==height && hash==i.xbox) return int(n);
  }
  return -1;
}
}
