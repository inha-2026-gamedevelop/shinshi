# 운영 웹 컨테이너

저장소 루트에서 `docker build -f deploy/admin-web/Dockerfile -t shinshi-admin-web .`로 빌드한다. .NET 10 공식 SDK/ASP.NET 런타임 이미지와 기존 계약 프로젝트를 사용하며 운영 웹 전용 NuGet 패키지를 추가하지 않는다. Blazor 브라우저 스크립트는 SDK가 암시적으로 추가하는 공식 `Microsoft.AspNetCore.App.Internal.Assets` 패키지에서 오므로 이미지 빌드의 restore 단계에서 nuget.org에 접근할 수 있어야 한다. 런타임은 공식 이미지의 `APP_UID` 비루트 사용자다. `/healthz`는 웹 프로세스 응답만 확인하며 DB/API 준비 상태를 보장하지 않는다.

설정·로컬 실행·운영자 흐름은 [Backend README 운영 웹](../../Backend/README.md#운영-웹-화면-adminweb)을 따른다. 이미지는 Production 환경에서 `ASPNETCORE_URLS=http://+:8080`으로 바인딩한다. appsettings에 `Urls`가 없으므로 환경 변수의 포트를 덮어쓰지 않는다. 내부 HTTP 포트는 8080이며 외부 접속은 사설망의 HTTPS 프록시를 거친다. 프록시에는 Blazor Interactive Server의 WebSocket 연결을 허용한다.

- `AdminWeb__ApiBaseUrl`에는 웹 컨테이너에서 접근할 API 주소를 넣는다. 기본 `http://127.0.0.1:5080`은 같은 호스트의 로컬 개발용이므로 별도 컨테이너에서는 API 서비스의 사설 DNS/포트를 지정한다.
- `AdminWeb__AllowedNetworks__*`에는 운영자 브라우저의 실제 IP/CIDR을 넣고 `ForwardedHeaders__KnownProxies__*`에는 웹 앞 HTTPS 프록시 IP만 넣는다. 신뢰 프록시는 허용 운영자 네트워크 밖이어야 하며 `X-Forwarded-For`와 `X-Forwarded-Proto`를 정확히 전달한다. 프록시 설정 없이 전달 헤더를 붙이면 요청은 404다.
- API의 `Admin__AllowedNetworks__*`에는 API가 보는 웹 서버 출처 IP를 별도로 허용한다. 웹은 브라우저의 전달 헤더를 API에 전송하지 않으며 기존 API 접근 정책을 바꾸지 않는다. API 감사의 요청 IP는 웹 서버 출처다.
- 현재 세션과 API bearer는 웹 프로세스 메모리에만 둔다. 단일 인스턴스로 배치하며 재시작하면 다시 로그인한다. 여러 인스턴스로 확장하려면 세션 저장소를 별도로 설계하고 연결에 sticky session을 적용한다.
- 프록시와 웹 사이 HTTP 연결에도 외부 HTTPS scheme 복원이 필요하다. 운영 세션 쿠키는 `__Host-` 이름과 `Secure`를 쓰고 HTTPS 응답에 HSTS를 설정하므로 외부 HTTP로 운영 로그인하지 않는다. 실제 비밀번호·TOTP·API bearer를 이미지, Dockerfile, 저장소 파일이나 명령줄에 넣지 않는다.

`AdminWeb__AllowedNetworks__*`를 명시하면 루프백 기본 허용도 대체된다. 이 Dockerfile의 내부 `/healthz` 검사에는 `127.0.0.1`을 추가로 허용해야 한다. 로컬 루프백은 컨테이너 내부 주소이며 신뢰 프록시 IP와 겹치지 않아야 한다.

```powershell
$env:ADMIN_WEB_API_URL = '<API private URL reachable from the container>'
$env:ADMIN_WEB_OPERATOR_NETWORK = '<operator IP or CIDR>'
$env:ADMIN_WEB_PROXY_IP = '<trusted HTTPS proxy IP outside operator networks>'
docker run --rm --name shinshi-admin-web `
  --publish 127.0.0.1:5081:8080 `
  --env "AdminWeb__ApiBaseUrl=$env:ADMIN_WEB_API_URL" `
  --env "AdminWeb__AllowedNetworks__0=$env:ADMIN_WEB_OPERATOR_NETWORK" `
  --env 'AdminWeb__AllowedNetworks__1=127.0.0.1' `
  --env "ForwardedHeaders__KnownProxies__0=$env:ADMIN_WEB_PROXY_IP" `
  shinshi-admin-web
```

위 명령은 자리표시자를 실제 배치 값으로 바꾸어 실행한다. 프록시가 다른 컨테이너/호스트에 있으면 공개 포트나 모든 인터페이스 바인딩으로 바로 노출하지 말고 실제 사설망 연결을 준비한다. 이미지 빌드·컨테이너 실행·HTTPS 프록시 뒤 운영 접속은 별도 실행 검증 대상이다.