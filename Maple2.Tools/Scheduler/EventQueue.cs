using System;
using System.Collections.Generic;
using System.Threading;
using Serilog;

namespace Maple2.Tools.Scheduler;

public class EventQueue {
    public bool Running { get; private set; }
    public int Queued => nextEvents.Count;
    public int Count => nextEvents.Count + timedEvents.Count;

    private readonly List<ScheduledEvent> timedEvents = [];
    private Queue<ScheduledEvent> nextEvents = [];
    private long nextTime = long.MaxValue;
    private readonly ILogger logger;

    private readonly object mutex = new object();

    public EventQueue(ILogger logger) {
        this.logger = logger;
    }

    public void Start() {
        Running = true;
    }

    public void Stop() {
        Running = false;
    }

    public void Clear() {
        lock (mutex) {
            timedEvents.Clear();
            nextEvents.Clear();
        }
    }

    /// <summary>
    /// Schedule a task to be run once
    /// </summary>
    /// <param name="task">task to be run</param>
    public void Schedule(Action task) {
        lock (mutex) {
            nextEvents.Enqueue(new ScheduledEvent(task));
        }

        Interlocked.Exchange(ref nextTime, 0);
    }

    /// <summary>
    /// Schedule a task to be run once after a specified delay
    /// </summary>
    /// <param name="task">task to be run</param>
    /// <param name="delay">delay after which the task will be run</param>
    public void Schedule(Action task, TimeSpan delay) {
        if (delay <= TimeSpan.Zero) {
            Schedule(task);
            return;
        }

        lock (mutex) {
            long executionTime = Environment.TickCount64 + (long) delay.TotalMilliseconds;
            timedEvents.Add(new ScheduledEvent(task, executionTime));

            Interlocked.Exchange(ref nextTime, Math.Min(nextTime, executionTime));
        }
    }

    /// <summary>
    /// Schedule a task to be executed repeatedly
    /// </summary>
    /// <param name="task">task to be run</param>
    /// <param name="interval">interval for this task to be run at</param>
    /// <param name="strict">if true, any delays will not affect future task scheduling</param>
    /// <param name="skipFirst">if true, the first execution will be skipped</param>
    public void ScheduleRepeated(Action task, TimeSpan interval, bool strict = false, bool skipFirst = false) {
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive");
        lock (mutex) {
            long executionTime = Environment.TickCount64;
            if (skipFirst) {
                executionTime += (long) interval.TotalMilliseconds;
            }
            timedEvents.Add(new ScheduledEvent(task, executionTime, interval, strict));

            Interlocked.Exchange(ref nextTime, Math.Min(nextTime, executionTime));
        }
    }

    public void InvokeAll() {
        // Avoid processing queue if we know nothing is available
        if (!Running || Environment.TickCount64 < nextTime) {
            return;
        }

        Queue<ScheduledEvent> events = nextEvents;
        lock (mutex) {
            nextEvents = new Queue<ScheduledEvent>();
            Interlocked.Exchange(ref nextTime, long.MaxValue);

            long timeNow = Environment.TickCount64;
            for (int i = timedEvents.Count - 1; i >= 0; i--) {
                if (timedEvents[i].Completed) {
                    timedEvents.RemoveAt(i);
                } else if (timedEvents[i].IsReady(timeNow)) {
                    events.Enqueue(timedEvents[i]);
                } else {
                    Interlocked.Exchange(ref nextTime, Math.Min(nextTime, timedEvents[i].ExecutionTime));
                }
            }
        }

        foreach (ScheduledEvent scheduledEvent in events) {
            try {
                long result = scheduledEvent.Invoke();
                if (result >= 0) {
                    Interlocked.Exchange(ref nextTime, Math.Min(nextTime, result));
                }
            } catch (Exception ex) {
                // Log exception but continue processing other events
                // The scheduler should be resilient to individual task failures
                logger.Error(ex, "[EventQueue] Task threw exception");

                // Even if task threw, we need to check if it has a next execution time
                // (for repeated tasks that should continue despite errors)
                if (scheduledEvent is { Completed: false, ExecutionTime: > 0 }) {
                    Interlocked.Exchange(ref nextTime, Math.Min(nextTime, scheduledEvent.ExecutionTime));
                }
            }
        }
    }
}
