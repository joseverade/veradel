; ============================================================================
;  Veradel para SolidWorks — instalador (Inno Setup 6)
;
;  Compilar:  ISCC.exe installer\VeradelSetup.iss
;  Requiere:  build Release del add-in en src\VeradeAddin\bin\Release\
;             (MSBuild ... /p:Configuration=Release)
;
;  Qué hace:
;    1. Aborta si SolidWorks esta abierto (la DLL quedaria bloqueada).
;    2. Copia el add-in + dependencias a Archivos de programa.
;    3. Registra la DLL en COM con regasm /codebase (RegisterFunction del
;       add-in escribe las claves HKLM\...\AddIns y HKCU\...\AddInsStartup).
;    4. Al desinstalar: regasm /unregister y borra todo.
; ============================================================================

#define AppName "Veradel para SolidWorks"
#define AppVersion "1.0.0"
#define AppPublisher "Veradel"
#define AddinDll "VeradeAddin.dll"
; GUID del add-in (coincide con AssemblyInfo / RegisterFunction). No cambiar.
#define AddinGuid "8B3E2A14-6C9D-4F1A-9E2B-7A5C1D3F8E40"
; Carpeta del build Release, relativa a este .iss
#define ReleaseDir "..\src\VeradeAddin\bin\Release"

[Setup]
; AppId estable: identifica el producto entre versiones (no cambiar nunca).
AppId={{C7E4F1A2-9B3D-4E6F-8A21-5D9C3B7E1F04}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Veradel
DefaultGroupName=Veradel
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=VeradelSetup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Registrar COM y escribir en HKLM requiere privilegios de administrador.
PrivilegesRequired=admin
; El add-in se carga como 64 bits dentro de SolidWorks.
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
UninstallDisplayName={#AppName}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "{#ReleaseDir}\{#AddinDll}";                       DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseDir}\Microsoft.Web.WebView2.Core.dll";   DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseDir}\Microsoft.Web.WebView2.WinForms.dll";DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseDir}\WebView2Loader.dll";                DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseDir}\data\*";                            DestDir: "{app}\data"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ReleaseDir}\web\*";                             DestDir: "{app}\web";  Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; Arranque automatico del add-in para el usuario que instala. regasm ya la
; escribe, pero la duplicamos en el contexto de instalacion por seguridad.
; Si el add-in no arranca solo, basta activarlo una vez en
; Herramientas > Complementos dentro de SolidWorks.
Root: HKCU; Subkey: "Software\SolidWorks\AddInsStartup\{{{#AddinGuid}}"; \
    ValueType: dword; ValueName: ""; ValueData: 1; Flags: uninsdeletekey

[Run]
; Registrar el add-in en COM (dispara RegisterFunction -> escribe las claves).
Filename: "{code:GetRegAsmPath}"; \
    Parameters: """{app}\{#AddinDll}"" /codebase"; \
    WorkingDir: "{app}"; Flags: runhidden waituntilterminated; \
    StatusMsg: "Registrando el complemento en SolidWorks..."

[UninstallRun]
; Desregistrar antes de borrar los archivos.
Filename: "{code:GetRegAsmPath}"; \
    Parameters: """{app}\{#AddinDll}"" /unregister"; \
    WorkingDir: "{app}"; Flags: runhidden waituntilterminated; RunOnceId: "UnregVeradelAddin"

[Code]
{ --- Ruta a RegAsm de .NET Framework 4.x, 64 bits --- }
function GetRegAsmPath(Param: String): String;
begin
  Result := ExpandConstant('{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe');
end;

{ --- Detecta si SLDWORKS.exe esta en ejecucion (find devuelve 0 si lo halla) --- }
function IsSolidWorksRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if Exec(ExpandConstant('{cmd}'),
          '/C tasklist /FI "IMAGENAME eq SLDWORKS.EXE" | find /I "SLDWORKS.EXE"',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0);
end;

{ --- Comprueba .NET Framework 4.8+ (Release >= 528040) --- }
function IsDotNet48Installed(): Boolean;
var
  Release: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
                        'Release', Release) then
    Result := (Release >= 528040);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if IsSolidWorksRunning() then
  begin
    MsgBox('SolidWorks esta abierto. Cierralo por completo y vuelve a ejecutar la instalacion.',
           mbError, MB_OK);
    Result := False;
    Exit;
  end;
  if not IsDotNet48Installed() then
  begin
    if MsgBox('No se detecta .NET Framework 4.8 (necesario para el complemento).' + #13#10 +
              'Puedes continuar, pero el complemento no funcionara hasta instalarlo.' + #13#10 +
              'Deseas continuar de todos modos?', mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if IsSolidWorksRunning() then
  begin
    MsgBox('SolidWorks esta abierto. Cierralo por completo y vuelve a ejecutar la desinstalacion.',
           mbError, MB_OK);
    Result := False;
  end;
end;
