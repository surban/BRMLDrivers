#pragma once

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>

#ifdef INTERFACE_CPP
#define DeclSpec __declspec(dllexport)
#else
#define DeclSpec __declspec(dllimport)
#endif

const int biotac_channels = 36;
typedef struct _biotac_frame
{
	uint16_t channel[biotac_channels];
} biotac_frame;


int DeclSpec biotac_init(unsigned int biotac_index);
void DeclSpec biotac_close();
void DeclSpec biotac_get_latest_data_array(biotac_frame *array, size_t *samples);

#ifdef __cplusplus
}
#endif