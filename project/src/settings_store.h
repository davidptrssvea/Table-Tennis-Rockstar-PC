#pragma once
#include <map>
#include <sstream>
#include <string>
namespace tabletennis::settings_store {
using Values = std::map<std::string, std::string>;
inline Values Parse(const std::string& text) {
  Values result;
  std::istringstream input(text);
  for (std::string line; std::getline(input,line);) {
    if (!line.empty() && line.back()=='\r') line.pop_back();
    auto equal=line.find('=');
    if(equal!=std::string::npos && !line.empty() && line[0]!='#' && line[0]!=';')
      result[line.substr(0,equal)]=line.substr(equal+1);
  }
  return result;
}
// Preserve comments, unknown keys, ISO/network paths and future settings.
// Replace duplicate known keys too, so the launcher and game agree.
inline std::string Merge(const std::string& text, const Values& updates) {
  auto remaining=updates;
  std::string result;
  std::istringstream input(text);
  for (std::string line; std::getline(input,line);) {
    if (!line.empty() && line.back()=='\r') line.pop_back();
    auto equal=line.find('=');
    auto found=updates.find(line.substr(0,equal));
    if(equal!=std::string::npos && found!=updates.end()) {
      line=found->first+"="+found->second; remaining.erase(found->first);
    }
    result+=line+"\r\n";
  }
  for(const auto& [key,value]:remaining) result+=key+"="+value+"\r\n";
  return result;
}
}
