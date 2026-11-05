#pragma once
#include <algorithm>
#include <string_view>
namespace tabletennis::pc::layout {
constexpr double kBaseAspect=16.0/9.0;
inline double Aspect(int width,int height) { return height>0 ? std::max(kBaseAspect,double(width)/height) : kBaseAspect; }
inline double DisplayAspect(int width,int height,std::string_view mode) {
  if(mode=="16:9") return kBaseAspect;
  if(mode=="21:9") return 21.0/9.0;
  if(mode=="32:9") return 32.0/9.0;
  return Aspect(width,height);
}
inline double PictureAspect(int width,int height,std::string_view mode,bool gameplay) {
  return gameplay?DisplayAspect(width,height,mode):kBaseAspect;
}
inline double UiScale(double aspect) { return kBaseAspect/std::max(kBaseAspect,aspect); }
inline double Camera(double original,double aspect) {
  return original>1.0 && original<2.0 ? original/UiScale(aspect) : original;
}
struct Rect { int left,top,right,bottom; };
inline Rect Intersect(Rect a,Rect b) {
  a.left=std::clamp(a.left,b.left,b.right);a.top=std::clamp(a.top,b.top,b.bottom);
  a.right=std::clamp(a.right,a.left,b.right);a.bottom=std::clamp(a.bottom,a.top,b.bottom);
  return a;
}
}
