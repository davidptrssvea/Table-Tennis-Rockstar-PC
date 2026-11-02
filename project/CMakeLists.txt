# tabletennis - ReXGlue Recompiled Project
#
# SDK boilerplate lives in generated/rexglue.cmake.

cmake_minimum_required(VERSION 3.25)
project(tabletennis LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

include(cmake/rexglue.cmake)

# Sources
set(TABLETENNIS_SOURCES
    src/main.cpp
    src/intro_skip.cpp
    src/frame_pacer.cpp
    src/stats_window.cpp
)

if(WIN32)
    add_executable(tabletennis WIN32 ${TABLETENNIS_SOURCES})
else()
    add_executable(tabletennis ${TABLETENNIS_SOURCES})
endif()

rexglue_setup_target(tabletennis)

if(WIN32)
    # frame_pacer: 1 ms timer resolution, DXGI Present hook.
    target_link_libraries(tabletennis PRIVATE winmm d3d11)
endif()

# The stock v0.10.0 application accepts directories only. Compile our BSD-licensed
# ISO-capable application adapter instead, without editing the installed SDK.
get_target_property(TABLETENNIS_HOST_SOURCES tabletennis SOURCES)
set(TABLETENNIS_SDK_APP "${REXGLUE_SHARE_DIR}/rex_app.cpp")
if(NOT TABLETENNIS_SDK_APP IN_LIST TABLETENNIS_HOST_SOURCES)
    message(FATAL_ERROR "SDK application source not found; cannot enable ISO support")
endif()
list(REMOVE_ITEM TABLETENNIS_HOST_SOURCES "${TABLETENNIS_SDK_APP}")
set_property(TARGET tabletennis PROPERTY SOURCES "${TABLETENNIS_HOST_SOURCES}")
target_sources(tabletennis PRIVATE sdk-overrides/rex_app.cpp)

# Apply the x64 FP exception-mask correction without modifying the installed SDK.
target_include_directories(tabletennis BEFORE PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/sdk-overrides")
if(TARGET tabletennis_recomp)
    target_include_directories(tabletennis_recomp BEFORE PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/sdk-overrides")
endif()
