// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

// Third Party
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace Shinshi.Admin.Web.Infrastructure
{
    /// <summary> 프로세스 세션과 동일하게 키 관리 서비스의 키도 메모리에만 보관한다 </summary>
    public sealed class AdminWebMemoryKeyRepository : IXmlRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly object _gate = new();
        private readonly List<XElement> _elements = new();

        /****************************************
        *                Methods
        ****************************************/

        public IReadOnlyCollection<XElement> GetAllElements()
        {
            lock (_gate)
            {
                return _elements.Select(element => new XElement(element)).ToArray();
            }
        }

        public void StoreElement(XElement element, string friendlyName)
        {
            ArgumentNullException.ThrowIfNull(element);
            lock (_gate)
            {
                _elements.Add(new XElement(element));
            }
        }
    }
}
