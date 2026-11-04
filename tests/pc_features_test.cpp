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
}
