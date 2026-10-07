// System
using System.Xml.Linq;

// Third Party
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

// Project
using Shinshi.Admin.Web.Infrastructure;
using Shinshi.Backend.Tests.Admin;

namespace Shinshi.Backend.Tests.AdminWeb
{
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class AdminWebMemoryKeyRepositoryTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void StoreElement_CallerMutation_DoesNotChangeStoredKey()
        {
            AdminWebMemoryKeyRepository repository = new();
            XElement key = new("key", new XAttribute("id", "test"));
            repository.StoreElement(key, "test");
            key.SetAttributeValue("id", "changed");

            Assert.Equal("test", Assert.Single(repository.GetAllElements()).Attribute("id")!.Value);
        }

        [Fact]
        public void GetAllElements_ResultMutation_DoesNotChangeStoredKeyOrOtherInstance()
        {
            AdminWebMemoryKeyRepository repository = new();
            repository.StoreElement(new XElement("key", new XAttribute("id", "test")), "test");
            Assert.Single(repository.GetAllElements()).SetAttributeValue("id", "changed");

            Assert.Equal("test", Assert.Single(repository.GetAllElements()).Attribute("id")!.Value);
            Assert.Empty(new AdminWebMemoryKeyRepository().GetAllElements());
        }

        [Fact]
        public void Startup_KeyManagement_UsesMemoryRepository()
        {
            using AdminWebTestFactory factory = new();
            KeyManagementOptions options = factory.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

            Assert.IsType<AdminWebMemoryKeyRepository>(options.XmlRepository);
        }
    }
}
