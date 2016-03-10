namespace BRML.Drivers
#nowarn "9"

open System
open System.Runtime.InteropServices


module BiotacNative =

    [<Struct>]
    [<type: StructLayout(LayoutKind.Sequential)>]
    type biotac_frame =
        [<MarshalAs(UnmanagedType.ByValArray, SizeConst=36)>]
        val mutable channel: uint16 []

    [<DllImport("biotac.dll")>]
    extern int biotac_init ()
    
    [<DllImport("biotac.dll")>]
    extern void biotac_close ()

    [<DllImport("biotac.dll")>]
    extern nativeint biotac_get_n_samples()

    [<DllImport("biotac.dll")>]
    extern nativeint biotac_get_latest_data_array (uint32 biotac_index, [<Out>] biotac_frame[] data, nativeint size)


[<AutoOpen>]
module Biotac =
    open BiotacNative

    /// Biotac sensor
    type BiotacT (index: int) =
        let mutable disposed = false

        // initialize biotac
        do
            let res = biotac_init()
            if res = 0 then failwith "Biotac initialization failed"

        /// maximum number of samples 
        let nMaxSamples = biotac_get_n_samples()

        /// Gets the latest available samples.
        let getSamples () =            
            // get samples
            let samples: biotac_frame[] = Array.zeroCreate (int nMaxSamples)
            let nSamples = biotac_get_latest_data_array (uint32 index, samples, nMaxSamples)

            // transform into sequence of samples
            seq {
                for n = 0 to (int nSamples) - 1 do
                    let smpl = samples.[n]
                    let channels = Array.zeroCreate 23
                    channels.[0..3]  <- smpl.channel.[0..3]
                    channels.[4..22] <- smpl.channel.[17..35]
                    yield Array.map int channels
            }

        /// get biotac data
        member this.GetSamples() = getSamples ()

        interface IDisposable with
            member this.Dispose() =               
                if not disposed then
                    biotac_close()
                    disposed <- true

        override this.Finalize() =
            if not disposed then
                biotac_close()

