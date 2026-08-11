#pragma once

#ifdef CONTROLLERINPUT_EXPORTS
#define CONTROLLERINPUT_API __declspec(dllexport)
#else
#define CONTROLLERINPUT_API __declspec(dllimport)
#endif

extern "C"
{
    CONTROLLERINPUT_API bool ControllerInput_Initialize();
}