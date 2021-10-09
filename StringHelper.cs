namespace RoslynFastStringSwitchPoc
{
    internal static class StringHelper
    {
        /// <summary>
        /// Returns a list of locations that should be read to generate identifiers.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static int GetUniqueColumnLocation(IEnumerable<string> input)
        {
            var inputArr = input.ToArray();
            if (inputArr.Length < 1)
            {
                throw new ArgumentException("Input must contain at least one string.", nameof(input));
            }

            if (inputArr.Select(str => str.Length).Distinct().Count() > 1)
            {
                throw new ArgumentException("All strings must have the same length.", nameof(input));
            }

            var strLength = inputArr[0].Length;
            var occurrences = new HashSet<char>[strLength];
            for (var idx = 0; idx < occurrences.Length; idx++)
            {
                occurrences[idx] = new HashSet<char>();
            }

            for (var arrIdx = 0; arrIdx < inputArr.Length; arrIdx++)
            {
                var str = inputArr[arrIdx];
                for (var strIdx = 0; strIdx < str.Length; strIdx++)
                {
                    var idxOccs = occurrences[strIdx];
                    idxOccs.Add(str[strIdx]);
                }
            }

            for (var idx = 0; idx < occurrences.Length; idx++)
            {
                if (occurrences[idx].Count == inputArr.Length)
                {
                    return idx;
                }
            }
            return -1;
        }
    }
}
