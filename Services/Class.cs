using System.Collections.Concurrent;

namespace ProxyVisterAPI.Services
{
    public interface ITimedTriggerService
    {
        void RegisterTickTrigger(Action action);
    }

    public class TimedTriggerService : BackgroundService, ITimedTriggerService
    {
        private readonly ConcurrentBag<Action> TickTriggers = new ConcurrentBag<Action>();
        private readonly ILogger<TimedTriggerService> Logger;
        private readonly TimeSpan TimeSpanInterval;

        public TimedTriggerService(IConfiguration Configuration, ILogger<TimedTriggerService> Logger)
        {
            this.Logger = Logger;
            int IntervalInMilliSeconds = Configuration.GetValue<int>("TimedTriggerIntervalInMilliSeconds", 20); // 默认为10秒
            TimeSpanInterval = TimeSpan.FromMilliseconds(IntervalInMilliSeconds);
        }

        public void RegisterTickTrigger(Action action)
        {
            TickTriggers.Add(action);
        }

        protected override async Task ExecuteAsync(CancellationToken StoppingToken)
        {
            Logger.LogInformation("Timed Trigger Service running.");

            while (!StoppingToken.IsCancellationRequested)
            {
                foreach (Action TickTrigger in TickTriggers)
                {
                    try
                    {
                        TickTrigger();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error executing TickTrigger in Timed Trigger Service.");
                    }
                }

                await Task.Delay(TimeSpanInterval, StoppingToken);
            }

            Logger.LogInformation("Timed Trigger Service is stopping.");
        }
    }
}
