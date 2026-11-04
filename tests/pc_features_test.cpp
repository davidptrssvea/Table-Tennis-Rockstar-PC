#include "button_texture_layout.h"
#include <array>
#include <set>
#include "settings_store.h"
#include "screen_layout.h"
#include <cassert>
#include <cmath>
int main() {
  using namespace tabletennis::pc::layout;
  auto near=[](double a,double b){return std::abs(a-b)<1e-9;};
  for(auto dims : {std::pair{1280,720},std::pair{2560,1080},std::pair{3440,1440},std::pair{3840,1600}}) {
    const double aspect=Aspect(dims.first,dims.second),scale=UiScale(aspect);
    // Square UI artwork must occupy equal physical distances on both axes.
    assert(near(scale*dims.first/1280.0,dims.second/720.0));
    assert(near(Camera(kBaseAspect,aspect),aspect));
    assert(near(Camera(1.0,aspect),1.0)); // square/shadow cameras unchanged
    const double inset=(1-scale)*1280/2;
    assert(near(inset+scale*1280/2,640)); // safe area stays centered
  }
  assert(near(Aspect(0,0),kBaseAspect));
  assert(near(UiScale(Aspect(1024,768)),1));
  const Rect bounds{160,0,1120,720};
  auto clip=Intersect({0,-100,1280,1000},bounds);
  assert(clip.left==160 && clip.top==0 && clip.right==1120 && clip.bottom==720);
  clip=Intersect({1500,0,1600,100},bounds);
  assert(clip.left==clip.right && clip.left==1120);
  clip=Intersect({-200,0,-10,100},bounds);
  assert(clip.left==clip.right && clip.left==160);
  {
    using namespace tabletennis::settings_store;
    const std::string original="# keep comment\r\niso_path=D:\\Games\\Table=Test.iso\r\nonline_server=192.0.2.1:27360\r\nshow_fps=false\r\nshow_fps=true\r\nfuture_key=keep\r\n";
    auto merged=Merge(original,{{"show_fps","false"},{"achievement_sound","false"}});
    auto values=Parse(merged);
    assert(values.at("show_fps")=="false");
    assert(values.at("achievement_sound")=="false");
    assert(values.at("iso_path")=="D:\\Games\\Table=Test.iso");
    assert(values.at("online_server")=="192.0.2.1:27360");
    assert(values.at("future_key")=="keep");
    assert(merged.find("# keep comment\r\n")!=std::string::npos);
    assert(Merge(merged,{{"show_fps","false"},{"achievement_sound","false"}})==merged);
  }
  {
    using namespace tabletennis::prompts::texture;
    constexpr uint64_t psCircle=777;
    const std::array<Signature,3> icons{{
      {64,64,0x7C7306ACF85A7575ULL,psCircle},
      {64,64,0xACBB72E1AFDE2EDCULL,psCircle},
      {128,64,0x164C3B7D492B2A4CULL,888}}};
    // The dialog's B is a separate image. A PS re-upload must keep that
    // identity so Xbox mode restores the exact dialog artwork, not menu B.
    assert(Identify(icons,64,64,0xACBB72E1AFDE2EDCULL)==1);
    assert(Identify(icons,64,64,psCircle,1)==1);
    assert(Identify(icons,64,64,psCircle)==-1);
    assert(Identify(icons,64,64,0x7C7306ACF85A7575ULL,1)==0);
    assert(Identify(icons,64,32,0xACBB72E1AFDE2EDCULL)==-1);
    assert(Identify(icons,128,64,0x164C3B7D492B2A4CULL)==2);
    for(auto shape:{std::pair{64u,32u},std::pair{64u,64u},std::pair{128u,64u},std::pair{128u,128u}}) {
      uint32_t maximum=0;std::set<uint32_t> offsets;
      for(uint32_t y=0;y<shape.second/4;++y) for(uint32_t x=0;x<shape.first/4;++x) {
        auto at=BlockOffset(x,y,shape.first/4);assert(at%16==0);offsets.insert(at);
        if(at>maximum) maximum=at;
      }
      assert(offsets.size()==size_t(shape.first/4)*(shape.second/4));
      assert(Fits(shape.first/4,shape.first,shape.second,maximum+16));
      assert(!Fits(shape.first/4,shape.first,shape.second,maximum+15));
      assert(!Fits(shape.first/4-1,shape.first,shape.second,maximum+16));
    }
  }
}
