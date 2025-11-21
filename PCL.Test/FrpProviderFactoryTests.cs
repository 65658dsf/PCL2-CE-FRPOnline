using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace PCL.Test;

[TestClass]
public class FrpProviderFactoryTests
{
    [TestMethod]
    public void DefaultLocalProvider()
    {
        var p = FrpProviderFactory.GetProvider(null);
        Assert.IsNotNull(p);
        Assert.AreEqual("LocalFrpProvider", p.GetType().Name);
    }

    [TestMethod]
    public void StellarProviderByKey()
    {
        var p = FrpProviderFactory.GetProvider("stellar");
        Assert.IsNotNull(p);
        Assert.AreEqual("StellarFrpProvider", p.GetType().Name);
    }
}