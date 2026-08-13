using System.Text;

namespace WhatsAppCampaignApi.Helpers;

public static class CsvHelper
{
    /// <summary>
    /// Splits one CSV line into its fields, honouring double-quoted fields that contain commas.
    ///
    /// Moved here verbatim from ContactsController, CampaignsController and CampaignService,
    /// which each carried an identical private copy. Three copies of the parser behind two
    /// importers that are expected to agree is a drift risk, and the contacts and campaign
    /// importers had already diverged elsewhere.
    /// </summary>
    public static List<string> SplitCsvRow(string line)
    {
        var result = new List<string>();
        var inQuotes = false;
        var currentField = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(currentField.ToString().Trim(' ', '"'));
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }
        result.Add(currentField.ToString().Trim(' ', '"'));
        return result;
    }
}
