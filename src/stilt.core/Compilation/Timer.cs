using System.Diagnostics;

namespace stilt.Compilation
{
    /// <summary>The pipeline stages the compiler measures; each maps to a <see cref="Timer"/> in the compiler's timer table.</summary>
    public enum TimedEvents
	{ Compilation, Lexing, Parsing, IRGeneration, Linking }

    /// <summary>A named stopwatch wrapper used to measure how long each compilation stage takes; <see cref="Run(Action)"/> times a delegate, and <see cref="Time"/> formats the elapsed result.</summary>
    public class Timer
	{
		private string _name;
		private Stopwatch? _stopwatch;

		public string Time => _stopwatch is null
			? $"{_name} has not been started."
			: _stopwatch.IsRunning
			? $"{_name} has been running for {_stopwatch.Elapsed.TotalSeconds}s."
			: $"{_name} finished in {_stopwatch.Elapsed.TotalSeconds}s.";

		public void StartTimer()
		{
			_stopwatch ??= new Stopwatch();
			_stopwatch.Start();
		}

		public void StopTimer()
		{
			_stopwatch?.Stop();
		}

		public void Run(Action action)
		{
			StartTimer();
			action.Invoke();
			StopTimer();
		}

		public T Run<T>(Func<T> func)
		{
			StartTimer();
			var result = func.Invoke();
			StopTimer();
			return result;
		}

		public Timer(string name, Action action)
		{
			_name = name;
			Run(action);
		}
		public Timer(string name)
		{
			_name = name;
		}
	}
}