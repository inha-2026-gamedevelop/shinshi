// System
using System.Runtime.CompilerServices;

// 테스트가 GameDB 정적 상태를 되돌릴 수 있게 한다
[assembly: InternalsVisibleTo("Shinshi.Tests.EditMode")]
[assembly: InternalsVisibleTo("Shinshi.Tests.PlayMode")]
