using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App;

namespace PCL.Test;

[TestClass]
public class ConfigLinkFrpProviderTest
{
    [TestMethod]
    public void DefaultIsLocal()
    {
        var val = Config.Link.FrpProvider;
        Assert.IsTrue(string.IsNullOrWhiteSpace(val) || val == "local");
    }

    [TestMethod]
    public void PersistSelection()
    {
        Config.Link.FrpProvider = "stellar";
        Assert.AreEqual("stellar", Config.Link.FrpProvider);

        Config.Link.FrpProvider = "local";
        Assert.AreEqual("local", Config.Link.FrpProvider);
    }
}