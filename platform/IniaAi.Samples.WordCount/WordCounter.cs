namespace IniaAi.Samples.WordCount;

/// <summary>The service a Count Words block needs. The block's strategy reads it from the scope the block runs in.</summary>
public interface IWordCounter
{
    int Count(string text);
}

public sealed class WordCounter : IWordCounter
{
    public int Count(string text)
    {
        int words = 0;
        bool inWord = false;
        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                words++;
            }
        }

        return words;
    }
}
