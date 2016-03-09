// based on example.c from BioTac

#define INTERFACE_CPP

#include <stdio.h>
#include <stdlib.h>
#include <Windows.h>
#include <vector>
#include <cassert>
#include <mutex>
#include <thread>
#include <condition_variable>
#include <iostream>

#include "cheetah.h"
#include "biotac.h"
#include "interface.h"




// interface variables
static bt_info biotac;
static bt_property biotac_property[MAX_BIOTACS_PER_CHEETAH];
static Cheetah ch_handle;

// recording thread
static std::thread recording_thread;
static volatile bool recorder_should_run;

// recorded data
typedef std::vector<biotac_frame> biotac_data_t;
static biotac_data_t latest_biotac_data;
static bool biotac_data_available;
static std::mutex biotac_data_mutex;
static std::condition_variable biotac_data_cv;

void on_termination();


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
	else
	{
		printf("\n%d BioTac(s) detected.\n\n", biotac.number_of_biotacs);
	}

	bt_init_frame_and_batch_info(&biotac);

	return true;
}


void biotac_set_latest_data(biotac_data_t data)
{
	std::lock_guard<std::mutex> lock(biotac_data_mutex);
	latest_biotac_data = data;
	biotac_data_available = true;
	biotac_data_cv.notify_all();
}

biotac_data_t biotac_get_latest_data()
{
	std::unique_lock<std::mutex> lock(biotac_data_mutex);
	biotac_data_cv.wait(lock, []{return biotac_data_available || (!recorder_should_run);});
	biotac_data_available = false;
	return latest_biotac_data;
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


void recording_thread_func(unsigned int biotac_index)
{
	// allocate batch buffer storage
	int n_samples = biotac.frame.frame_size * biotac.batch.batch_frame_count;
	bt_data *batch = bt_configure_save_buffer(n_samples);

	// configure biotac
	int bt_err_code = bt_cheetah_configure_batch(ch_handle, &biotac, n_samples);
	if (bt_err_code < 0)
	{
		bt_display_errors(bt_err_code);
		return;
	}

	// recording loop
	while (recorder_should_run)
	{
		// get "raw" data
		bt_cheetah_collect_batch(ch_handle, &biotac, batch, NO);

		// convert to sensible format
		biotac_data_t data = extract_biotac_data(biotac_index, batch, n_samples);

		// store
		biotac_set_latest_data(data);
	}

	free(batch);
}


void biotac_start_recording(unsigned int biotac_index)
{
	recorder_should_run = true;
	recording_thread = std::thread(recording_thread_func, biotac_index);
}

void biotac_stop_recording()
{
	recorder_should_run = false;
	biotac_data_cv.notify_all();
	recording_thread.join();
}

void pin_dll()
{
	HMODULE hmod;
	GetModuleHandleEx(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
	 				  reinterpret_cast<LPCTSTR>(&pin_dll),
					  &hmod);
}


int biotac_init(unsigned int biotac_index)
{
	if (!biotac_hardware_init())
		return 0;
	pin_dll();
	biotac_start_recording(biotac_index);
	std::atexit(on_termination);
	return 1;
}

void biotac_close()
{
	biotac_stop_recording();
	bt_cheetah_close(ch_handle);
}

void on_termination()
{
	printf("on_terminatnion32\n");
	//std::this_thread::sleep_for(std::chrono::milliseconds(500));
	if (recorder_should_run)
		biotac_close();
	//std::this_thread::sleep_for(std::chrono::milliseconds(500));
	printf("termination done\n");
}

void DeclSpec biotac_get_latest_data_array(biotac_frame *array, size_t *samples)
{
	biotac_data_t data = biotac_get_latest_data();
	if (*samples >= data.size() && array != NULL)
	{
		*samples = data.size();
		for (size_t i = 0; i < data.size(); i++)
		{
			//printf("Sample %d:", i);
			//for (size_t ch = 0; ch < biotac_channels; ch++)
			//	printf("%04x ", data[i].channel[0]);
			//printf("\n");

			array[i] = data[i];
		}
	} else {
		*samples = data.size();
	}
}


/*
extern "C" {
	extern __declspec(noinline) BOOL __cdecl _DllMainCRTStartup(HANDLE  hDllHandle, DWORD   dwReason, LPVOID  lpreserved);
	__declspec(noinline) BOOL __cdecl MyDllEntryPoint(HANDLE  hDllHandle, DWORD   dwReason, LPVOID  lpreserved)
	{
		if (dwReason == DLL_PROCESS_DETACH)
			return TRUE;
		else
			return _DllMainCRTStartup(hDllHandle, dwReason, lpreserved);
	}
}
*/
