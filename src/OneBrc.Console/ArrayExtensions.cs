namespace OneBrc.Console;

public static class ArrayExtensions
{
    extension(Array arr)
    {
        public static T[] Create<T>(int n, Func<int, T> initElement)
        {
            var result = new T[n];

            for (int i = 0; i < n; i++)
            {
                result[i] = initElement(i);
            }

            return result;
        }
    }
}
