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
    extern int biotac_init (uint32 biotac_index)
    
    [<DllImport("biotac.dll")>]
    extern void biotac_close ()

    [<DllImport("biotac.dll")>]
    extern void biotac_get_latest_data_array ([<In; Out>] biotac_frame[] data, nativeint& size)


[<AutoOpen>]
module Biotac =
    open BiotacNative

    /// Biotac sensor
    type BiotacT (index: int) =
        inherit System.Runtime.ConstrainedExecution.CriticalFinalizerObject()

        let mutable disposed = false

        do
            let res = biotac_init(uint32 index)
            if res = 0 then failwith "Biotac initialization failed"

        /// Gets the latest available samples.
        let getLatestSamples () =
            // get number of available samples
            let mutable nSamples = nativeint 0
            biotac_get_latest_data_array (null, &nSamples)

            // get samples
            let samples: biotac_frame[] = Array.zeroCreate (int nSamples)
            biotac_get_latest_data_array (samples, &nSamples)

            // transform into sequence of samples
            seq {
                for smpl in samples do
                    let channels = Array.zeroCreate 23
                    channels.[0..3]  <- smpl.channel.[0..3]
                    channels.[4..22] <- smpl.channel.[17..35]
                    yield Array.map int channels
            }

        /// latest channel data
        // We are exposing this as a property, because the underlying C++ code does buffering
        // and no new samples are fetched from the sensor.
        member this.Samples = getLatestSamples ()

        interface IDisposable with
            member this.Dispose() =
                ()
//                if not disposed then
//                    //biotac_close()
//                    disposed <- true

        override this.Finalize() =
            printfn "finalizer"
            //if not disposed then
            //    biotac_close()

