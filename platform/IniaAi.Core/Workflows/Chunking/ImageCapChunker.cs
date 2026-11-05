using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Core.Workflows.Chunking;

public static class ImageCapChunker
{
    public static Dictionary<string, Dictionary<int, Page>> CapByImageCount(
        Dictionary<string, Dictionary<int, Page>> groups,
        int maxImagesPerRequest)
    {
        if (maxImagesPerRequest <= 0)
        {
            return groups;
        }

        Dictionary<string, Dictionary<int, Page>> capped = [];
        int outputIndex = 0;

        foreach (Dictionary<int, Page> group in groups.Values)
        {
            var currentGroup = new Dictionary<int, Page>();
            int currentImageCount = 0;

            foreach ((int index, Page page) in group.OrderBy(kvp => kvp.Key))
            {
                bool pageHasImage = page.Image is not null;

                if (pageHasImage && currentImageCount >= maxImagesPerRequest)
                {
                    capped[$"chunk_{outputIndex}"] = currentGroup;
                    outputIndex++;
                    currentGroup = [];
                    currentImageCount = 0;
                }

                currentGroup[index] = page;

                if (pageHasImage)
                {
                    currentImageCount++;
                }
            }

            if (currentGroup.Count > 0)
            {
                capped[$"chunk_{outputIndex}"] = currentGroup;
                outputIndex++;
            }
        }

        return capped;
    }
}
