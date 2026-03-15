# CMakeLists.txt for SysExec
# Requires CMake 3.15+ and the MSVC toolchain (cl.exe).
#
# Typical build (from repo root):
#   cmake -S src/SysExec -B build/SysExec -G "Visual Studio 17 2022" -A x64
#   cmake --build build/SysExec --config Release
#
# Or from a VS Developer Command Prompt:
#   mkdir build && cd build
#   cmake .. -G "NMake Makefiles" -DCMAKE_BUILD_TYPE=Release
#   cmake --build .

cmake_minimum_required(VERSION 3.15)

project(SysExec
    VERSION     1.0.0
    DESCRIPTION "Lightweight NT AUTHORITY\\SYSTEM / remote command executor"
    LANGUAGES   CXX)

# -----------------------------------------------------------------------
# Source target
# -----------------------------------------------------------------------
add_executable(SysExec SysExec.cpp)

# Unicode entry point
target_compile_definitions(SysExec PRIVATE
    _UNICODE
    UNICODE
    WIN32_LEAN_AND_MEAN
    _WIN32_WINNT=0x0600   # Vista+ minimum
)

# -----------------------------------------------------------------------
# Compiler options (MSVC)
# -----------------------------------------------------------------------
if(MSVC)
    target_compile_options(SysExec PRIVATE
        /W4         # High warning level
        /WX         # Warnings as errors

        # Static CRT: /MT for Release, /MTd for Debug
        $<$<CONFIG:Debug>:/MTd>
        $<$<NOT:$<CONFIG:Debug>>:/MT>

        # Optimization flags – only for non-Debug configs.
        # In Debug, CMake already injects /Od /RTC1 which conflict with /O2.
        $<$<NOT:$<CONFIG:Debug>>:/O2>   # Full optimization
        $<$<NOT:$<CONFIG:Debug>>:/Oi>   # Enable intrinsic functions
        $<$<NOT:$<CONFIG:Debug>>:/GL>   # Whole-program optimization
    )

    target_link_options(SysExec PRIVATE
        /SUBSYSTEM:CONSOLE
        /MANIFEST:NO           # No side-by-side manifest needed

        # /LTCG requires /GL; restrict to the same non-Debug configs
        $<$<NOT:$<CONFIG:Debug>>:/LTCG>
    )
endif()

# -----------------------------------------------------------------------
# Required Windows libraries
# -----------------------------------------------------------------------
target_link_libraries(SysExec PRIVATE
    advapi32    # Service Control Manager, registry, token APIs
    kernel32
    user32
)

# -----------------------------------------------------------------------
# Install rule
# Installs SysExec.exe to CMAKE_INSTALL_PREFIX/bin (default).
# Override with: cmake --install <builddir> --prefix <destination>
# -----------------------------------------------------------------------
install(TARGETS SysExec
    RUNTIME DESTINATION bin
)
