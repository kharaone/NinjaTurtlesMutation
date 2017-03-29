using System;
using NUnit.Engine;
namespace NinjaTurtlesMutation
{
	public class NullListener : ITestEventListener
	{
		public NullListener()
		{
		}

		public void OnTestEvent(string report)
		{
		}
	}
}
