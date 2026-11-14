using System.Globalization;
using DataControls.Tests.Support;

namespace DataControls.Tests.Integration;

/// <summary>
/// The engine must return exactly what the hand-written queries in database/99_verify.sql return.
/// Definitions come from ctl.Controls; expectations are parsed from 99_verify.sql.
/// </summary>
[Trait("Category", "Integration")]
public class SampleControlsMatchVerifyScriptTests
{
    [IntegrationTheory]
    [MemberData(nameof(DatabaseScripts.ControlNumbers), MemberType = typeof(DatabaseScripts))]
    public async Task Engine_returns_the_expected_violations(int controlNumber)
    {
        var expected = DatabaseScripts.ExpectedViolations()[controlNumber];

        var result = await IntegrationDb.CreateService().RunControlAsync(controlNumber);

        var actual = result.Violations.Select(v => Convert.ToInt32(v.Id, CultureInfo.InvariantCulture)).Order().ToArray();
        Assert.Equal(expected, actual);
        Assert.Equal(expected.Length, result.TotalCount);
        Assert.False(result.Truncated);
        Assert.NotNull(result.RunId);

        Assert.All(result.Violations, v =>
        {
            Assert.Matches("[א-ת]", v.Reason);
            Assert.NotEqual(Application.Results.ViolationReasonBuilder.FallbackReason, v.Reason);
            Assert.NotEmpty(v.Values);
        });
    }
}
