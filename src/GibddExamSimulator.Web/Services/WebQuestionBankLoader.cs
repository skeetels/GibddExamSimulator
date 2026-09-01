using GibddExamSimulator.Mobile.Shared.Services;

namespace GibddExamSimulator.Web.Services;

public sealed class WebQuestionBankLoader(HttpClient httpClient) : IMobileQuestionBankLoader
{
    public async Task<MobileQuestionBank> LoadAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await httpClient.GetByteArrayAsync("question-bank/ab/bank-manifest.json", cancellationToken);
        var questions = await httpClient.GetByteArrayAsync("question-bank/ab/official-questions.json", cancellationToken);
        return MobileQuestionBankParser.Parse(manifest, questions);
    }
}
