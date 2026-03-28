using System.Diagnostics;

namespace slate.Compilation
{
    public enum TimedEvents
	{ Compilation, Lexing, Parsing, IRGeneration, Linking }

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