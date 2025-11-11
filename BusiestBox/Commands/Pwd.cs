using System;

namespace BusiestBox.Commands
{
    internal class Pwd
    {
        public static void Execute(string currentDirectory)
        {
            Console.WriteLine(currentDirectory);
        }
    }
}
