namespace JobScheduleSample

open System
open Android.App
open Android.Content
open Android.OS
open Android.Runtime
open Android.Views
open Android.Widget
open Android.App.Job
open Android.Util
open Android.Content
open Java.Lang
open Android.Content

module JobSchedulerConstants = 
    let FibonacciJobId = 110
    let FibonacciValueKey = "fibonacci_value"
    let FibonacciResultKey = "fibonacci_result"
    let FibonacciJobActionKey = "fibonacci_job_action"


[<Service(Name = "JobScheduleSample.FibonacciJob", Permission = "android.permission.BIND_JOB_SERVICE")>]
type FibonacciJob() as this = 
    inherit JobService()
    let TAG = typeof<FibonacciJob>.FullName

    member val Parameters: JobParameters option = None with get,set
    member val Calculator: SimpleFibonacciCalculatorTask option = None with get,set

    member this.BroadcastResults (result: int64) = 
        Log.Debug(TAG, sprintf "Computed value: %d" result) |> ignore
        let i = new Intent(JobSchedulerConstants.FibonacciJobActionKey);
        i.PutExtra(JobSchedulerConstants.FibonacciResultKey, result) 
        |> this.BaseContext.SendBroadcast


    override x.OnStartJob(``params``) = 
        let fibonacciValue = ``params``.Extras.GetLong(JobSchedulerConstants.FibonacciValueKey, -1L);

        if fibonacciValue < 0L then 
            Log.Debug(TAG, "Invalid value - must be > 0.") |> ignore
            false
        else 
            this.Parameters <- Some ``params``;
            this.Calculator <-  new SimpleFibonacciCalculatorTask(this) |> Some


            this.Calculator |> Option.iter (fun calculator -> 
                calculator.Execute(fibonacciValue) |> ignore)
            true

    override x.OnStopJob(jobParams) = 
        Log.Debug(TAG, "System halted the job.") |> ignore

        this.Calculator |>  Option.iter (fun calculator -> 
            if not calculator.IsCancelled then 
                calculator.Cancel true |> ignore)

        this.Calculator <- None

        this.BroadcastResults -1L
        false

and 
    SimpleFibonacciCalculatorTask(jobService:FibonacciJob) =
        inherit AsyncTask<int64, Java.Lang.Void, int64>()
        let TAG = typeof<SimpleFibonacciCalculatorTask>.FullName

        let mutable fibonacciValue = -1L;

        let getFibonacciFor(value:int64) = 
            if value = 0L then 0L
            elif value = 1L || value = 2L then 1L
            else 
                let mutable result = 0L
                let mutable n1 = 0L
                let mutable n2 = 1L

                for i in seq { for x in 2L .. value do if x%2L = 0L then yield x } do 
                    Thread.Sleep 1000L
                    result <- n1 + n2
                    n1 <- n2
                    n2 <- result

                result

        override this.RunInBackground(``params``) = 
            fibonacciValue <- -1L;
            ``params``.[0] |> getFibonacciFor

        override this.OnPostExecute(result:int64) = 
            base.OnPostExecute(result)

            fibonacciValue <- result;

            jobService.BroadcastResults(result)

            jobService.Parameters |> Option.iter (fun parameters -> 
                jobService.JobFinished(parameters, false))

            Log.Debug(TAG, "Finished with fibonacci calculation: " + result.ToString()) |> ignore

        override this.OnCancelled() = 
            Log.Debug(TAG, "Job was cancelled.") |> ignore
            jobService.BroadcastResults(-1L)
            base.OnCancelled()

module JobSchedulerHelpers = 

    let GetComponentNameForJob<'T when 'T :> JobService>(context:Context) = 
        new ComponentName(context, typeof<'T> |> Class.FromType)

    let setFibonacciValue(value:int64) (builder: JobInfo.Builder) = 
        let extras = new PersistableBundle();
        extras.PutLong(JobSchedulerConstants.FibonacciValueKey, value);
        builder.SetExtras(extras)

    let CreateJobInfoBuilderForFibonnaciCalculation(context: Context) (value: int64 ) = 
        context
        |> GetComponentNameForJob<FibonacciJob>
        |> (fun comp -> new JobInfo.Builder(JobSchedulerConstants.FibonacciJobId, comp))
        |> setFibonacciValue value
type Callback = int64 option -> unit

[<BroadcastReceiver(Enabled = true, Exported = false)>]
type FibonacciResultReciever(callback:Callback option) =
    inherit BroadcastReceiver()
    let TAG = typeof<FibonacciResultReciever>.FullName
    new() = new FibonacciResultReciever(None) 

    override x.OnReceive(context:Context , intent:Intent) = 

        Log.Debug(TAG, "Received broadcast") |> ignore

        match callback with 
        | None -> 
            Log.Warn(TAG, "There is no activity, ignoring the results.") |> ignore
        | Some callback -> 

            let result = intent.Extras.GetLong(JobSchedulerConstants.FibonacciResultKey, -1L);
            if result > -1L then
                Some result |> callback 
            else
                callback None

[<Activity (Label = "Jobber", MainLauncher = true, Icon = "@mipmap/icon")>]
type MainActivity () as this =
    inherit Activity ()
    let TAG = typeof<MainActivity>.FullName

    let mutable jobScheduler: JobScheduler = null
    let mutable receiver:FibonacciResultReciever option = None
    let mutable resultsTextView:TextView = null
    let mutable inputEditText:EditText = null
    let mutable calculateButton:Button = null

    let callback result = 
        Log.Debug(TAG, sprintf "Callback has been called with result: %A" result) |> ignore
        match result with 
        | Some (result: int64) -> 
            let formatArgs: Java.Lang.Object[] = [|Java.Lang.Long.ValueOf(result.ToString()) |]
            resultsTextView.Text <- this.Resources.GetString(Resources.String.fibonacci_calculation_result, formatArgs)
        | None -> 
            resultsTextView.SetText(Resources.String.fibonacci_calculation_problem);
        calculateButton.Enabled <- true


    member this.ScheduleFibonacciCalculation (eventArgs:EventArgs) = 
        let value = Int64.Parse inputEditText.Text
        let builder = (JobSchedulerHelpers.CreateJobInfoBuilderForFibonnaciCalculation this value)
                            .SetPersisted(false)
                            .SetMinimumLatency(1000L)    // Wait at least 1 second
                            .SetOverrideDeadline(5000L)  // But no longer than 5 seconds
                            .SetRequiredNetworkType(NetworkType.Unmetered);

        let result = jobScheduler.Schedule(builder.Build())
        if result = JobScheduler.ResultSuccess then 
            calculateButton.Enabled <- false;
            resultsTextView.SetText(Resources.String.fibonacci_calculation_in_progress);
            Log.Debug(TAG, "Job started!") |> ignore
        else
            Log.Warn(TAG, "Problem starting the job " + result.ToString()) |> ignore

    override this.OnCreate (bundle) =
        base.OnCreate (bundle)
        receiver <- new FibonacciResultReciever(Some callback) |> Some

        jobScheduler <- this.GetSystemService(JobService.JobSchedulerService) :?> JobScheduler

        // Set our view from the "main" layout resource
        this.SetContentView(Resources.Layout.Main);

        resultsTextView <- this.FindViewById<TextView>(Resources.Id.results_textview);
        inputEditText <- this.FindViewById<EditText>(Resources.Id.fibonacci_start_value);

        calculateButton <- this.FindViewById<Button>(Resources.Id.download_button);
        calculateButton.Click.Add this.ScheduleFibonacciCalculation

    override this.OnResume () = 
        base.OnResume()

        receiver |> Option.iter (fun receiver -> 
            this.BaseContext.RegisterReceiver(receiver, new IntentFilter(JobSchedulerConstants.FibonacciResultKey)) |> ignore)

        let filter = new IntentFilter();
        filter.AddAction(JobSchedulerConstants.FibonacciJobActionKey);
        receiver |> Option.iter (fun receiver -> this.RegisterReceiver(receiver, filter) |> ignore)

    override this.OnPause() = 
        receiver |> Option.iter (fun receiver -> this.BaseContext.UnregisterReceiver(receiver))
        base.OnPause()

