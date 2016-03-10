// based on example.c from BioTac

#define INTERFACE_CPP

#include <stdio.h>
#include <stdlib.h>
#include <Windows.h>
#include <vector>
#include <cassert>

#include "cheetah.h"
#include "biotac.h"
#include "interface.h"



// interface variables
static bt_info biotac;
static bt_property biotac_property[MAX_BIOTACS_PER_CHEETAH];
static Cheetah ch_handle;

// biotac buffer
int biotac_n_samples;
bt_data *biotac_batch;

// recorded data
typedef std::vector<biotac_frame> biotac_data_t;


bool biotac_hardware_init()
{
	BioTac bt_err_code;

	/**************************************************************************/
	/* --- Initialize BioTac settings (only default values are supported) --- */
	/**************************************************************************/
	biotac.spi_clock_speed = BT_SPI_BITRATE_KHZ_DEFAULT;
	biotac.number_of_biotacs = 0;
	biotac.sample_rate_Hz = BT_SAMPLE_RATE_HZ_DEFAULT;
	biotac.frame.frame_type = 0;
	biotac.batch.batch_frame_count = BT_FRAMES_IN_BATCH_DEFAULT;
	biotac.batch.batch_ms = BT_BATCH_MS_DEFAULT;

	// Check if any initial settings are wrong
	if (MAX_BIOTACS_PER_CHEETAH != 3 && MAX_BIOTACS_PER_CHEETAH != 5)
	{
		bt_err_code = BT_WRONG_MAX_BIOTAC_NUMBER;
		bt_display_errors(bt_err_code);
		return false;
	}

	/******************************************/
	/* --- Initialize the Cheetah devices --- */
	/******************************************/
	ch_handle = bt_cheetah_initialize(&biotac);

	/*********************************************************/
	/* --- Get and print out properties of the BioTac(s) --- */
	/*********************************************************/
	for (int i = 0; i < MAX_BIOTACS_PER_CHEETAH; i++)
	{
		bt_err_code = bt_cheetah_get_properties(ch_handle, i + 1, &(biotac_property[i]));

		if (biotac_property[i].bt_connected == YES)
		{
			(biotac.number_of_biotacs)++;
		}

		if (bt_err_code)
		{
			bt_display_errors(bt_err_code);
			return false;
		}
	}

	// Check if any BioTacs are detected
	if (biotac.number_of_biotacs == 0)
	{
		bt_err_code = BT_NO_BIOTAC_DETECTED;
		bt_display_errors(bt_err_code);
		return false;
	}

	bt_init_frame_and_batch_info(&biotac);

	return true;
}


bool biotac_buffer_init()
{
	// allocate batch buffer storage
	biotac_n_samples = biotac.frame.frame_size * biotac.batch.batch_frame_count;
	biotac_batch = bt_configure_save_buffer(biotac_n_samples);

	// configure biotac
	int bt_err_code = bt_cheetah_configure_batch(ch_handle, &biotac, biotac_n_samples);
	if (bt_err_code < 0)
	{
		bt_display_errors(bt_err_code);
		return false;
	}

	return true;
}

size_t biotac_get_n_samples()
{
	return biotac_n_samples;
}

biotac_data_t extract_biotac_data(unsigned int biotac_index, const bt_data *batch, size_t n_samples)
{
	size_t samples_per_frame = biotac.frame.frame_size;
	assert(n_samples % samples_per_frame == 0);
	size_t n_frames = n_samples / biotac.frame.frame_size;

	std::vector<biotac_frame> frames(n_frames);
	for (size_t frame_no = 0; frame_no < n_frames; frame_no++)
	{
		biotac_frame &frame = frames[frame_no];

		for (size_t channel_no = 0; channel_no < biotac_channels; channel_no++)
			frame.channel[channel_no] = 0;

		for (size_t frame_sample_no = 0; frame_sample_no < samples_per_frame; frame_sample_no++)
		{
			size_t sample_no = frame_no * samples_per_frame + frame_sample_no;
			assert(batch[sample_no].channel_id < biotac_channels);
			frame.channel[batch[sample_no].channel_id] = batch[sample_no].d[biotac_index].word;
		}
	}

	return frames;
}


biotac_data_t biotac_get_data(unsigned int biotac_index)
{
	// get "raw" data
	bt_cheetah_collect_batch(ch_handle, &biotac, biotac_batch, NO);

	// convert to sensible format
	return extract_biotac_data(biotac_index, biotac_batch, biotac_n_samples);
}

int biotac_init()
{
	if (!biotac_hardware_init())
		return 0;
	if (!biotac_buffer_init())
		return 0;
	return 1;
}

void biotac_close()
{
	free(biotac_batch);

	// closing the handle sometimes hangs the process, so we leave it open.
	//bt_cheetah_close(ch_handle);
}

size_t DeclSpec biotac_get_latest_data_array(unsigned int biotac_index, biotac_frame *array, size_t samples)
{
	biotac_data_t data = biotac_get_data(biotac_index);
	samples = min(samples, data.size());
	for (size_t i = 0; i < samples; i++)
	{
		//printf("Sample %d:", i);
		//for (size_t ch = 0; ch < biotac_channels; ch++)
		//	printf("%04x ", data[i].channel[0]);
		//printf("\n");

		array[i] = data[i];
	}
	return samples;
}


