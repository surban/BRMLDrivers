namespace BRML.Drivers

open System
open System.IO
open System.IO.Ports
open System.Threading
open System.Text.RegularExpressions


/// low-level driver for RS485 Nanotec stepper motor drivers
module Stepper =

    type StepperIdT = int

    type ConfigT = {
        Id:             StepperIdT;
        AnglePerStep:   float;
        StepMode:       int;    
    }

    type StepperT = 
        {Port:           SerialPort;
         Config:         ConfigT;}

        member this.Id = this.Config.Id
        member this.AnglePerStep = this.Config.AnglePerStep
        member this.StepMode = this.Config.StepMode

    type StatusT = {
        Ready:          bool;
        ZeroPos:        bool;
        PosErr:         bool;
    }

    type PositioningModeT =
        | Relative
        | Absolute
        | ExternalReferencing
        | Velocity

    type DirectionT =
        | Left
        | Right


    type private MsgT = Msg of string * int option
        
    let private sendMsg (Msg (cmd, arg)) (stepper: StepperT) =
        let argStr =
            match arg with
            | Some arg -> sprintf "%+d" arg
            | None -> ""
        let msgStr = sprintf "#%d%s%s\r" stepper.Id cmd argStr
        //printfn "sending: %s" msgStr
        stepper.Port.Write msgStr

    let private receiveMsg (stepper: StepperT) = 
        let msgStr = stepper.Port.ReadTo "\r"
        //printfn "received: %s" msgStr

        let m = Regex.Match(msgStr, @"(\d+)([!-*,./:-~]+)([+-]?\d+)?$")
        if not m.Success then
            failwithf "Stepper received malformed reply message: %s" msgStr
        let id = int m.Groups.[1].Value
        let cmd = m.Groups.[2].Value
        let arg = 
            if m.Groups.Count > 3 && String.length m.Groups.[3].Value > 0 then Some (int m.Groups.[3].Value)
            else None
        if id <> stepper.Id then
            failwithf "Stepper received reply with mismatching id: %s" msgStr

        Msg (cmd, arg)

    let private sendAndReceive msg stepper =
        sendMsg msg stepper
        receiveMsg stepper

    let private sendAndReceiveArg (Msg (msgCmd, _) as msg) stepper =
        let (Msg (replyCmd, replyArg) as reply) = sendAndReceive msg stepper
        if replyCmd <> msgCmd then
            failwithf "Stepper received reply %A with mismatching command for msg %A" reply msg
        match replyArg with
        | Some a -> a
        | None -> failwithf "Stepper received reply %A without argument for msg %A" reply msg
        
    let private sendAndConfirm msg stepper =
        let reply = sendAndReceive msg stepper
        if reply <> msg then
            failwithf "Stepper got wrong confirmation %A for message %A" reply msg

    let private isBitSet bit x = x &&& (1 <<< bit) <> 0 

    let private accelToArg accel =
        int ((3000. / ((float accel) / 1000. + 11.7) ** 2.))

    let private angleToSteps (angle: float) (stepper: StepperT) =
        angle * (float stepper.StepMode) / stepper.AnglePerStep |> int

    let private stepsToAngle (steps: int) (stepper: StepperT) =
        (float steps) * stepper.AnglePerStep / (float stepper.StepMode)

    let getStatus stepper =
        let r = sendAndReceiveArg (Msg ("$", None)) stepper
        {Ready=isBitSet 0 r; ZeroPos=isBitSet 1 r; PosErr=isBitSet 2 r;}

    let isReady stepper =
        let {Ready=ready} = getStatus stepper
        ready

    let setPositioningMode mode stepper =
        let arg =
            match mode with
            | Relative -> 1
            | Absolute -> 2
            | ExternalReferencing -> 4
            | Velocity -> 5
        sendAndConfirm (Msg("p", Some arg)) stepper

    let setDirection direction stepper =
        let arg =
            match direction with
            | Left -> 0
            | Right -> 1
        sendAndConfirm (Msg("d", Some arg)) stepper

    let getPosSteps stepper =               sendAndReceiveArg (Msg ("C", None)) stepper
    let isReferenced stepper =              sendAndReceiveArg (Msg (":is_referenced", None)) stepper = 1
    let setTargetPos pos stepper =          sendAndConfirm (Msg ("s", Some pos)) stepper
    let setStartStepsPerSec sps stepper =   sendAndConfirm (Msg ("u", Some sps)) stepper
    let setDriveStepsPerSec sps stepper =   sendAndConfirm (Msg ("o", Some sps)) stepper
    let setAccel accel stepper =            sendAndConfirm (Msg ("b", Some (accelToArg accel))) stepper
    let setDecel decel stepper =            sendAndConfirm (Msg ("B", Some (accelToArg decel))) stepper
    let setRepetitions reps stepper =       sendAndConfirm (Msg ("W", Some reps)) stepper
    let setFollowCmd cmd stepper =          sendAndConfirm (Msg ("N", Some cmd)) stepper  
    let resetPosErr pos stepper =           sendAndConfirm (Msg ("D", Some pos)) stepper
    let start stepper =                     sendAndConfirm (Msg ("A", None)) stepper
    let stop stepper =                      sendAndConfirm (Msg ("S", Some 0)) stepper
    let quickStop stepper =                 sendAndConfirm (Msg ("S", Some 1)) stepper


    let getPos stepper =
        let p = getPosSteps stepper 
        stepsToAngle p stepper

    let driveTo pos vel accel decel stepper =
        setPositioningMode Absolute stepper
        setTargetPos (angleToSteps pos stepper) stepper
        setStartStepsPerSec 1 stepper
        setDriveStepsPerSec (angleToSteps vel stepper) stepper
        setAccel (angleToSteps accel stepper) stepper
        setDecel (angleToSteps decel stepper) stepper
        setRepetitions 1 stepper
        setFollowCmd 0 stepper
        start stepper
        
    let splitDirection vel =
        if vel >= 0. then vel, Right
        else -vel, Left

    let startConstantVelocityDrive vel accel decel stepper =
        let absVel, dir = splitDirection vel

        setPositioningMode Velocity stepper
        setDirection dir stepper
        setStartStepsPerSec 1 stepper
        setDriveStepsPerSec (angleToSteps absVel stepper) stepper
        setAccel (angleToSteps accel stepper) stepper
        setDecel (angleToSteps decel stepper) stepper
        setRepetitions 1 stepper
        setFollowCmd 0 stepper
        start stepper
        
    let adjustVelocity vel stepper =
        let absVel, dir = splitDirection vel
        setDirection dir stepper
        setDriveStepsPerSec (angleToSteps absVel stepper) stepper

    let adjustAccelDecel accel decel stepper =
        setAccel (angleToSteps accel stepper) stepper
        setDecel (angleToSteps decel stepper) stepper

    let externalReferencing direction vel accel stepper =
        setPositioningMode ExternalReferencing stepper
        setDirection direction stepper
        setStartStepsPerSec 1 stepper
        setDriveStepsPerSec (angleToSteps vel stepper) stepper
        setAccel (angleToSteps accel stepper) stepper
        setDecel (angleToSteps accel stepper) stepper
        setRepetitions 1 stepper
        setFollowCmd 0 stepper
        start stepper


module XYTable =

    type AxisConfigT = {
        StepperConfig:  Stepper.ConfigT;
        DegPerMM:       float;
        Home:           Stepper.DirectionT;
        MaxPos:         float;
    }

    type XYTableConfigT = {
        PortName:       string;
        PortBaud:       int;
        X:              AxisConfigT;
        Y:              AxisConfigT;
        DefaultVel:     float;
        DefaultAccel:   float;
        HomeVel:        float;
    }

    type XYTupleT = float * float

    type MsgT =
        | MsgHome
        | MsgDriveTo of XYTupleT * XYTupleT * XYTupleT * XYTupleT
        | MsgDriveWithVel of XYTupleT * XYTupleT * XYTupleT
        | MsgStop of XYTupleT

    type MsgWithReplyT = MsgT * AsyncReplyChannel<unit>

    type XYTableT (config: XYTableConfigT) =

        let port = new SerialPort(config.PortName, config.PortBaud)     
        do
            port.Open()  

        let xStepper = {Stepper.StepperT.Port=port; Stepper.StepperT.Config=config.X.StepperConfig}
        let yStepper = {Stepper.StepperT.Port=port; Stepper.StepperT.Config=config.Y.StepperConfig}

        let mutable disposed = false

        let quickStop () =
            for i = 1 to 10 do
                Stepper.quickStop xStepper
                Stepper.quickStop yStepper

        let posInRange (x, y) =
            0. <= x && x <= config.X.MaxPos && 0. <= y && y <= config.Y.MaxPos

        [<VolatileField>]
        let mutable currentPos = Stepper.getPos xStepper, Stepper.getPos yStepper
        
        [<VolatileField>]
        let mutable currentStatus = Stepper.getStatus xStepper, Stepper.getStatus yStepper

        [<VolatileField>]
        let mutable overshoot = false
        
        [<VolatileField>]
        let mutable homed = false

        [<VolatileField>]
        let mutable sentinelThreadShouldRun = true

        let readyEventInt = new Event<_>()
        let readyEvent = readyEventInt.Publish

        let sentinelThread = Thread(fun () -> 
            while sentinelThreadShouldRun do
                lock port (fun () ->
                    currentPos <- Stepper.getPos xStepper, Stepper.getPos yStepper                    
                    currentStatus <- Stepper.getStatus xStepper, Stepper.getStatus yStepper
                    
                    let xStatus, yStatus = currentStatus
                    if xStatus.Ready && yStatus.Ready then
                        readyEventInt.Trigger()

                    if homed && not (posInRange currentPos) then
                        overshoot <- true
                        quickStop ()
                )        
                Thread.Yield() |> ignore                
        )

        let waitForReady() = async {
            let xStatus, yStatus = currentStatus
            if not (xStatus.Ready && yStatus.Ready) then
                do! Async.AwaitEvent readyEvent
        }

        let agent =
            MailboxProcessor<MsgWithReplyT>.Start(fun inbox ->
                async { 
                    while true do
                        let vmActive = ref false
                        let vmVel = ref (0., 0.)
                        let vmAccel = ref (0., 0.)
                        let vmDecel = ref (0., 0.)

                        let! msg, rc = inbox.Receive()
                        if overshoot then failwith "XYTable position overshoot"
                        match msg with
                        | MsgHome ->
                            sentinelThread.Start()
                            lock port (fun () ->
                                Stepper.externalReferencing config.X.Home config.HomeVel config.DefaultAccel xStepper
                                Stepper.externalReferencing config.Y.Home config.HomeVel config.DefaultAccel yStepper
                            )                            
                            do! waitForReady()
                            homed <- true
                            rc.Reply()

                        | MsgDriveTo (pos, vel, accel, decel) ->
                            if not homed then failwith "XYTable not homed"

                            let (xpos, ypos), (xvel, yvel), (xaccel, yaccel), (xdecel, ydecel) = 
                                pos, vel, accel, decel
                            lock port (fun () ->
                                Stepper.driveTo xpos xvel xaccel xdecel xStepper
                                Stepper.driveTo ypos yvel yaccel ydecel yStepper
                            )
                            do! waitForReady()
                            rc.Reply()

                        | MsgDriveWithVel (vel, accel, decel) ->
                            if not homed then failwith "XYTable not homed"

                            let (xvel, yvel), (xaccel, yaccel), (xdecel, ydecel) = 
                                vel, accel, decel

                            lock port (fun () ->
                                if not !vmActive then
                                    Stepper.startConstantVelocityDrive xvel xaccel xdecel xStepper
                                    Stepper.startConstantVelocityDrive yvel yaccel ydecel yStepper
                                    vmActive := true
                                    vmVel := vel
                                    vmAccel := accel
                                    vmDecel := decel
                                else
                                    if accel <> !vmAccel || decel <> !vmDecel then
                                        Stepper.adjustAccelDecel xaccel xdecel xStepper
                                        Stepper.adjustAccelDecel yaccel ydecel yStepper
                                        vmAccel := accel
                                        vmDecel := decel
                                    if vel <> !vmVel then
                                        Stepper.adjustVelocity xvel xStepper
                                        Stepper.adjustVelocity yvel yStepper
                                        vmVel := vel
                            )
                            rc.Reply()

                        | MsgStop decel ->      
                            if not homed then failwith "XYTable not homed"

                            let (xaccel, yaccel), (xdecel, ydecel) = !vmAccel, decel    
                            
                            lock port (fun () ->     
                                if decel <> !vmDecel then
                                    Stepper.adjustAccelDecel xaccel xdecel xStepper
                                    Stepper.adjustAccelDecel yaccel ydecel yStepper
                                Stepper.stop xStepper
                                Stepper.stop yStepper
                                vmActive := false
                            )
                            do! waitForReady()
                            rc.Reply()
                }           
            )

        let postMsg msg = agent.PostAndAsyncReply(fun rc -> msg, rc)


        member this.Pos = currentPos
        
        member this.Home() = 
            postMsg (MsgHome)

        member this.DriveTo (pos, ?vel, ?accel, ?decel) = 
            let vel = defaultArg vel (config.DefaultVel, config.DefaultVel)
            let accel = defaultArg accel (config.DefaultAccel, config.DefaultAccel)
            let decel = defaultArg decel (config.DefaultAccel, config.DefaultAccel)
            postMsg (MsgDriveTo (pos, vel, accel, decel))

        member this.DriveWithVel (vel, ?accel, ?decel) = 
            let accel = defaultArg accel (config.DefaultAccel, config.DefaultAccel)
            let decel = defaultArg decel (config.DefaultAccel, config.DefaultAccel)       
            postMsg (MsgDriveWithVel (vel, accel, decel))

        member this.Stop (?decel) = 
            let decel = defaultArg decel (config.DefaultAccel, config.DefaultAccel)       
            postMsg (MsgStop (decel))

        interface IDisposable with
            member this.Dispose () =
                sentinelThreadShouldRun <- false
                sentinelThread.Join ()
                quickStop ()
                port.Dispose()
                disposed <- true

        override this.Finalize() =
            if not disposed then
                sentinelThreadShouldRun <- false
                sentinelThread.Join ()
                quickStop ()


[<AutoOpen>]
module XYTableTypes =
    type XYTableConfigT = XYTable.XYTableConfigT
    type XYTableT = XYTable.XYTableT



