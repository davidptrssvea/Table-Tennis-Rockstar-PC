#pragma once
#include <algorithm>
namespace tabletennis::pc::layout {
constexpr double kBaseAspect=16.0/9.0;
inline double Aspect(int width,int height) { return height>0 ? std::max(kBaseAspect,double(width)/height) : kBaseAspect; }
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
