#pragma warning disable

public class SmokeTest
{
    [Test]
    public async Task TestSmoke()
    {
        await Assert.That(true).IsTrue();
    }
}
