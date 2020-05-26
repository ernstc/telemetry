using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;

namespace EasyTest
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestMethod1()
        {
            Assert.IsTrue(true);
            //Assert.AreEqual(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
        }
    }
}
