#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TCS.SceneManagement {
    public class ParallelAwaitables {
        readonly List<Func<IProgress<float>?, CancellationToken, Awaitable>> m_awaitableFactories = new();
        readonly IProgress<float>? m_overallProgress;

        /// <summary>
        /// Initializes a new instance of the ParallelAwaitables class.
        /// </summary>
        /// <param name="overallProgress">
        /// An optional overall progress reporter. If null, progress aggregation is skipped.
        /// </param>
        public ParallelAwaitables(IProgress<float>? overallProgress = null) {
            m_overallProgress = overallProgress;
        }

        /// <summary>
        /// Adds an awaitable factory to the container.
        /// </summary>
        /// <param name="factory">
        /// A function that takes an optional IProgress<float> and a cancellation token, and returns an Awaitable.
        /// </param>
        /// <returns>The container itself for fluent chaining.</returns>
        public ParallelAwaitables Add(Func<IProgress<float>?, CancellationToken, Awaitable> factory) {
            if (factory == null) {
                throw new ArgumentNullException(nameof(factory));
            }

            m_awaitableFactories.Add(factory);
            return this;
        }

        public ParallelAwaitables Add(Func<IProgress<float>?, CancellationToken, Task> factory) {
            if (factory == null) {
                throw new ArgumentNullException(nameof(factory));
            }

            return Add((progress, token) => ConvertTaskToAwaitable(factory(progress, token)));
        }

        static async Awaitable ConvertTaskToAwaitable(Task task) {
            await task;
        }


        /// <summary>
        /// Runs all added awaitable tasks in parallel.
        /// If an overall progress reporter was provided, aggregated progress is reported.
        /// The cancellation token is optional.
        /// </summary>
        /// <param name="token">An optional cancellation token (default is CancellationToken.None).</param>
        /// <returns>An Awaitable that completes when all tasks complete.</returns>
        public Awaitable RunAll(CancellationToken token = default) {
            return AwaitableParallelTaskRunner.RunParallelAsync(m_awaitableFactories, m_overallProgress, token);
        }
    }

    public static class AwaitableExtensions {
        /// <summary>
        /// Returns an awaitable that completes when all the provided awaitables complete.
        /// If any awaitable throws, aggregates the exceptions into an AggregateException.
        /// </summary>
        public static async Awaitable WhenAll(this IEnumerable<Awaitable> awaitables) {
            List<Exception>? exceptions = null;
            foreach (var awaitable in awaitables) {
                try {
                    await awaitable;
                }
                catch (Exception ex) {
                    exceptions ??= new List<Exception>();
                    exceptions.Add(ex);
                }
            }

            if (exceptions != null) {
                throw new AggregateException(exceptions);
            }
        }
    }

    public static class AwaitableParallelTaskRunner {
        /// <summary>
        /// Runs multiple awaitable tasks in parallel and optionally aggregates their progress.
        /// </summary>
        /// <param name="awaitableFactories">
        /// A list of functions that start an awaitable task and optionally report progress via IProgress&lt;float&gt;.
        /// </param>
        /// <param name="overallProgress">
        /// An optional IProgress&lt;float&gt; instance that receives the aggregated progress.
        /// If null, progress reporting is skipped.
        /// </param>
        /// <param name="token">
        /// An optional cancellation token for the tasks (default is CancellationToken.None).
        /// </param>
        /// <returns>An Awaitable that completes when all tasks complete.</returns>
        public static Awaitable RunParallelAsync(
            List<Func<IProgress<float>?, CancellationToken, Awaitable>> awaitableFactories,
            IProgress<float>? overallProgress,
            CancellationToken token = default
        ) {
            if (awaitableFactories == null) {
                throw new ArgumentNullException(nameof(awaitableFactories));
            }

            List<Awaitable> awaitables = new(awaitableFactories.Count);
            if (overallProgress != null) {
                // With progress aggregation: create an aggregator and pass sub-progress reporters.
                var aggregator = new ParallelProgressAggregator(awaitableFactories.Count, overallProgress);
                for (var i = 0; i < awaitableFactories.Count; i++) {
                    IProgress<float> subProgress = aggregator.CreateSubProgress(i);
                    awaitables.Add(awaitableFactories[i](subProgress, token));
                }
            }
            else {
                // Without progress aggregation: simply pass null.
                awaitables.AddRange(awaitableFactories.Select(t => t(null, token)));
            }

            return awaitables.WhenAll();
        }
    }

    public class ParallelProgressAggregator {
        readonly object m_lock = new();
        readonly int m_subtaskCount;
        readonly IProgress<float> m_overallProgress;
        readonly float[] m_subtaskProgresses;

        /// <summary>
        /// Initializes a new instance of the ParallelProgressAggregator.
        /// </summary>
        /// <param name="subtaskCount">The total number of subtasks to aggregate.</param>
        /// <param name="overallProgress">The overall progress reporter.</param>
        public ParallelProgressAggregator(int subtaskCount, IProgress<float> overallProgress) {
            if (subtaskCount <= 0) {
                throw new ArgumentOutOfRangeException(nameof(subtaskCount), "Subtask count must be greater than zero.");
            }

            m_subtaskCount = subtaskCount;
            m_overallProgress = overallProgress ?? throw new ArgumentNullException(nameof(overallProgress));
            m_subtaskProgresses = new float[subtaskCount];
        }

        /// <summary>
        /// Creates a sub-progress reporter for the subtask at the given index.
        /// </summary>
        /// <param name="index">Index of the subtask.</param>
        /// <returns>An IProgress&lt;float&gt; that, when reported to, updates the overall progress.</returns>
        public IProgress<float> CreateSubProgress(int index) {
            if (index < 0 || index >= m_subtaskCount) {
                throw new ArgumentOutOfRangeException(nameof(index), "Index must be within the range of subtasks.");
            }

            return new Progress<float>
            (
                value => {
                    lock (m_lock) {
                        // Clamp the reported progress to [0, 1]
                        m_subtaskProgresses[index] = Mathf.Clamp01(value);

                        // Compute the average progress of all subtasks.
                        float sum = 0f;
                        for (var i = 0; i < m_subtaskCount; i++) {
                            sum += m_subtaskProgresses[i];
                        }

                        float average = sum / m_subtaskCount;
                        m_overallProgress.Report(average);
                    }
                }
            );
        }
    }
}