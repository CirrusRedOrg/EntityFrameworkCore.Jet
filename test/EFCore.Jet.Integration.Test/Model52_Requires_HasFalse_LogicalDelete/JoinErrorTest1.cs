using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model52_Requires_HasFalse_LogicalDelete
{
    [TestClass]
    public class Model52_Requires_HasFalse_LogicalDelete
    {
        [TestMethod]
        public void JetTest()
        {
            using (Context context = new Context(Helpers.GetJetConnection()))
            {
                // This test actually does not work because of this configuration
                // modelBuilder.Entity<Texture>().Map(m => m.Requires("IsDeleted").HasValue(false)).Ignore(m => m.IsDeleted);

                int panelId = 12;
                // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                context.PanelTextures.AsNoTracking().Where(pt => pt.PanelId == panelId).Select(pt => pt.Texture).ToList();
            }
        }
    }
}
