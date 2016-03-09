namespace BRML.Drivers

open System
open System.IO
open System.IO.Ports
open System.Threading
open System.Text.RegularExpressions


/// low-level driver for RS485 Nanotec stepper motor drivers
module Stepper =

    [<Literal>]
    let Debug = false

    type StepperIdT = int

    type ConfigT = {
        Id:             StepperIdT;
        AnglePerStep:   float;
        StepMode:       int;    
        StartVel:       float;
    }

    type StepperT = 
        {Port:           SerialPort;
         Config:         ConfigT;}

        member this.Id = this.Config.Id
        member this.AnglePerStep = this.Config.AnglePerStep
        member this.StepMode = this.Config.StepMode
        member this.StartVel = this.Config.StartVel

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
        //if Debug then printfn "sending: %s" msgStr
        stepper.Port.Write msgStr

    let private receiveMsg (stepper: StepperT) = 
        let msgStr = stepper.Port.ReadTo "\r"
        //if Debug then printfn "received: %s" msgStr

        let m = Regex.Match(msgStr, @"^(\d+)([!-*,./:-~]+)([+-]?\d+)?$")
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
        int ((3000. / ((float accel) / 1000. + 11.7)) ** 2.)

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
    let stop stepper =                      sendAndConfirm (Msg ("S", Some 1)) stepper
    let quickStop stepper =                 sendAndConfirm (Msg ("S", Some 0)) stepper


    let getPos stepper =
        let p = getPosSteps stepper 
        stepsToAngle p stepper

    let driveTo pos vel accel decel stepper =
        setPositioningMode Absolute stepper
        setTargetPos (angleToSteps pos stepper) stepper
        setStartStepsPerSec (angleToSteps stepper.StartVel stepper) stepper
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
        setStartStepsPerSec (angleToSteps stepper.StartVel stepper) stepper
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
        setStartStepsPerSec (angleToSteps stepper.StartVel stepper) stepper
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
        | MsgDriveWithVel of XYTupleT * XYTupleT 
        | MsgStop of XYTupleT

    type ReplyT =
        | ReplyOk
        | ReplyNotHomed
        | ReplyOutOfRange

    type MsgWithReplyT = MsgT * AsyncReplyChannel<ReplyT>

    type XYTableT (config: XYTableConfigT) =
        inherit System.Runtime.ConstrainedExecution.CriticalFinalizerObject()

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
            -0.2 <= x && x <= config.X.MaxPos && -0.2 <= y && y <= config.Y.MaxPos

        [<VolatileField>]
        let mutable currentPos = 0., 0.
        
        [<VolatileField>]
        let mutable currentStatus = Stepper.getStatus xStepper, Stepper.getStatus yStepper

        [<VolatileField>]
        let mutable overshoot = false
        
        [<VolatileField>]
        let mutable homed = false

        [<VolatileField>]
        let mutable sentinelThreadShouldRun = true

        let fetchPos () = 
            let xPos, yPos = Stepper.getPos xStepper, Stepper.getPos yStepper
            let xPos = if config.X.Home = Stepper.Right then -xPos else xPos
            let yPos = if config.Y.Home = Stepper.Right then -yPos else yPos
            xPos / config.X.DegPerMM, yPos / config.Y.DegPerMM

        let fetchStatus () =
            Stepper.getStatus xStepper, Stepper.getStatus yStepper

        let readyEventInt = new Event<_>()
        let readyEvent = readyEventInt.Publish

        let sentinelThread = Thread(fun () -> 
            while sentinelThreadShouldRun do
                lock port (fun () ->
                    currentPos <- fetchPos ()
                    currentStatus <- fetchStatus ()
                    
                    let xStatus, yStatus = currentStatus
                    if xStatus.Ready && yStatus.Ready then
                        readyEventInt.Trigger()

                    if Stepper.Debug then
                        printfn "X status: %A" xStatus
                        printfn "Y status: %A" yStatus 
                        printfn "Position: %A" currentPos

                    if homed && not (posInRange currentPos) then
                        printfn "====== XYTable overshoot: %A" currentPos 
                        overshoot <- true
                        quickStop ()
                        exit -1

                    if xStatus.PosErr || yStatus.PosErr then
                        printfn "====== XYTable position error"
                        quickStop ()
                        exit -1
                )        
                Thread.Yield() |> ignore     
                if Stepper.Debug then Thread.Sleep(500)           
        )

        let waitForReady() = async {
            do! Async.AwaitEvent readyEvent
        }

        let agent =
            MailboxProcessor<MsgWithReplyT>.Start(fun inbox ->
                async { 
                    while true do
                        let vmActive = ref false
                        let vmVel = ref (0., 0.)
                        let vmAccel = ref (0., 0.)
                        let xDeg mm = config.X.DegPerMM * mm
                        let yDeg mm = config.Y.DegPerMM * mm

                        let! msg, rc = inbox.Receive()                       
                        match msg with
                        | MsgHome ->
                            lock port (fun () ->
                                let {Stepper.PosErr=xPosErr}, {Stepper.PosErr=yPosErr} = fetchStatus ()
                                let xHomed, yHomed = Stepper.isReferenced xStepper, Stepper.isReferenced yStepper
                                let pos = fetchPos ()

                                if (not xHomed) || (not yHomed) || xPosErr || yPosErr || (not (posInRange pos)) then
                                    Stepper.resetPosErr 1000 xStepper
                                    Stepper.resetPosErr 1000 yStepper
                                    Stepper.externalReferencing config.X.Home (xDeg config.HomeVel) 
                                        (xDeg config.DefaultAccel) xStepper
                                    Stepper.externalReferencing config.Y.Home (yDeg config.HomeVel) 
                                        (yDeg config.DefaultAccel) yStepper
                            )                            

                            sentinelThread.Start()
                            do! waitForReady()
                            do! Async.Sleep 100
                            homed <- true
                            rc.Reply ReplyOk

                        | MsgDriveTo (((xpos, ypos) as pos), (xvel, yvel), (xaccel, yaccel), (xdecel, ydecel)) ->
                            if not (posInRange pos) then rc.Reply ReplyOutOfRange
                            if not homed then rc.Reply ReplyNotHomed
                            else 
                                let xpos = if config.X.Home = Stepper.Right then -xpos else xpos
                                let ypos = if config.Y.Home = Stepper.Right then -ypos else ypos
                                lock port (fun () ->
                                    Stepper.driveTo (xDeg xpos) (xDeg xvel) (xDeg xaccel) (xDeg xdecel) xStepper
                                    Stepper.driveTo (yDeg ypos) (yDeg yvel) (yDeg yaccel) (yDeg ydecel) yStepper
                                )
                                do! waitForReady()
                                rc.Reply ReplyOk

                        | MsgDriveWithVel ((xvel, yvel as vel), (xaccel, yaccel as accel)) ->
                            if not homed then rc.Reply ReplyNotHomed
                            else
                                lock port (fun () ->
                                    if not !vmActive then
                                        Stepper.startConstantVelocityDrive (xDeg xvel) (xDeg xaccel) (xDeg xaccel) xStepper
                                        Stepper.startConstantVelocityDrive (yDeg yvel) (yDeg yaccel) (yDeg yaccel) yStepper
                                        vmActive := true
                                        vmVel := vel
                                        vmAccel := accel
                                    else
                                        if accel <> !vmAccel then
                                            Stepper.adjustAccelDecel (xDeg xaccel) (xDeg xaccel) xStepper
                                            Stepper.adjustAccelDecel (yDeg yaccel) (yDeg yaccel) yStepper
                                            vmAccel := accel
                                        if vel <> !vmVel then
                                            Stepper.adjustVelocity (xDeg xvel) xStepper
                                            Stepper.adjustVelocity (yDeg yvel) yStepper
                                            vmVel := vel
                                )
                                rc.Reply ReplyOk

                        | MsgStop (xaccel, yaccel as accel) ->      
                            lock port (fun () ->     
                                if accel <> !vmAccel then
                                    Stepper.adjustAccelDecel (xDeg xaccel) (xDeg xaccel) xStepper
                                    Stepper.adjustAccelDecel (yDeg yaccel) (yDeg yaccel) yStepper
                                    vmAccel := accel
                                Stepper.stop xStepper
                                Stepper.stop yStepper
                                vmActive := false
                            )
                            do! waitForReady()
                            rc.Reply ReplyOk
                }           
            )

        let postMsg msg = async {
            let! reply = agent.PostAndAsyncReply(fun rc -> msg, rc)
            match reply with
            | ReplyOk -> ()
            | ReplyNotHomed -> failwith "XYTable not homed"
            | ReplyOutOfRange -> failwith "XYTable position out of range"
        }

        let terminate () =
            //printfn "Terminate"
            if not disposed then
                sentinelThreadShouldRun <- false                
                sentinelThread.Join ()
                quickStop ()                     

        do
            AppDomain.CurrentDomain.ProcessExit.Add(fun _ -> terminate())
            AppDomain.CurrentDomain.UnhandledException.Add(fun _ -> terminate())
            AppDomain.CurrentDomain.DomainUnload.Add(fun _ -> terminate())
            Console.CancelKeyPress.Add(fun _ -> terminate())

        member this.Pos = currentPos
        
        member this.Home() = 
            postMsg (MsgHome)

        member this.DriveTo (pos, ?vel, ?accel, ?decel) = 
            let vel = defaultArg vel (config.DefaultVel, config.DefaultVel)
            let accel = defaultArg accel (config.DefaultAccel, config.DefaultAccel)
            let decel = defaultArg decel (config.DefaultAccel, config.DefaultAccel)
            postMsg (MsgDriveTo (pos, vel, accel, decel))

        member this.DriveWithVel (vel, ?accel) = 
            let accel = defaultArg accel (config.DefaultAccel, config.DefaultAccel)
            postMsg (MsgDriveWithVel (vel, accel)) |> Async.RunSynchronously

        member this.Stop (?accel) = 
            let accel = defaultArg accel (config.DefaultAccel, config.DefaultAccel)       
            postMsg (MsgStop (accel)) |> Async.RunSynchronously


        interface IDisposable with
            member this.Dispose () =
                terminate ()
                port.Dispose()
                disposed <- true

        override this.Finalize() =
            terminate ()





[<AutoOpen>]
module XYTableTypes =
    type XYTableConfigT = XYTable.XYTableConfigT
    type XYTableT = XYTable.XYTableT




