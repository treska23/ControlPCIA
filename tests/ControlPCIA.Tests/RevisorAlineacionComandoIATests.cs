using Xunit;

namespace ControlPCIA.Tests;

public sealed class RevisorAlineacionComandoIATests
{
    [Theory]
    [InlineData(
        "Get-StartApps | Where-Object Name -Like '*Cubase*' | Select-Object Name,AppID")]
    [InlineData(
        "Get-Command -Name 'cubase*' -ErrorAction SilentlyContinue")]
    [InlineData(
        "Get-ChildItem -LiteralPath 'C:\\Datos' -Filter '*.cpr' -File -Recurse | Select-Object -First 20 FullName")]
    public void Permite_consultas_de_investigacion_sin_efectos(
        string comando)
    {
        Assert.True(
            RevisorAlineacionComandoIA
                .EsConsultaInvestigacionSegura(comando));
    }

    [Theory]
    [InlineData(
        "Get-StartApps; Set-ItemProperty -Path HKCU:\\Software\\X -Name Y -Value 1")]
    [InlineData(
        "Get-ChildItem C:\\; [System.IO.File]::WriteAllText('C:\\nota.txt','x')")]
    [InlineData(
        "Start-Process Cubase.exe")]
    public void No_disfraza_una_accion_como_investigacion(
        string comando)
    {
        Assert.False(
            RevisorAlineacionComandoIA
                .EsConsultaInvestigacionSegura(comando));
    }

    [Theory]
    [InlineData("Stop-Process -Name msedge")]
    [InlineData("taskkill.exe /IM msedge.exe")]
    [InlineData("(Get-Process msedge).Kill()")]
    public void No_cierra_un_proceso_si_la_tarea_no_lo_pide(
        string comando)
    {
        var plan = new PlanTareasControl(
            ["maximizar Microsoft Edge"]);

        RevisionAlineacionComando revision =
            RevisorAlineacionComandoIA
                .ValidarContradiccionesEvidentes(
                    plan,
                    new HashSet<int> { 1 },
                    comando);

        Assert.False(revision.Alineado);
    }

    [Theory]
    [InlineData("Start-Process msinfo32")]
    [InlineData("Start-Process SystemPropertiesAdvanced")]
    [InlineData("control.exe sysdm.cpl")]
    public void No_abre_propiedades_del_sistema_para_otra_tarea(
        string comando)
    {
        var plan = new PlanTareasControl(
            ["activar Microsoft Edge"]);

        RevisionAlineacionComando revision =
            RevisorAlineacionComandoIA
                .ValidarContradiccionesEvidentes(
                    plan,
                    new HashSet<int> { 1 },
                    comando);

        Assert.False(revision.Alineado);
    }

    [Fact]
    public void Permite_cerrar_cuando_es_la_tarea_pendiente()
    {
        var plan = new PlanTareasControl(
            ["cerrar Microsoft Edge"]);

        RevisionAlineacionComando revision =
            RevisorAlineacionComandoIA
                .ValidarContradiccionesEvidentes(
                    plan,
                    new HashSet<int> { 1 },
                    "Stop-Process -Name msedge");

        Assert.True(revision.Alineado);
    }

    [Fact]
    public void Permite_abrir_cuando_es_la_tarea_pendiente()
    {
        var plan = new PlanTareasControl(
            ["abrir Cubase"]);

        RevisionAlineacionComando revision =
            RevisorAlineacionComandoIA
                .ValidarContradiccionesEvidentes(
                    plan,
                    new HashSet<int> { 1 },
                    "Start-Process -FilePath 'C:\\Cubase.exe'");

        Assert.True(revision.Alineado);
    }

    [Theory]
    [InlineData(
        """{"alineado":true,"motivo":"Consulta necesaria."}""",
        true)]
    [InlineData(
        """Texto {"alineado":false,"motivo":"Objetivo distinto."} fin""",
        false)]
    public void Extrae_la_revision_estructurada(
        string respuesta,
        bool esperado)
    {
        RevisionAlineacionComando revision = Assert.IsType<
            RevisionAlineacionComando>(
            RevisorAlineacionComandoIA.ExtraerRevision(
                respuesta));

        Assert.Equal(esperado, revision.Alineado);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sí")]
    [InlineData("""{"permitido":true}""")]
    public void No_acepta_una_revision_sin_formato(
        string respuesta)
    {
        Assert.Null(
            RevisorAlineacionComandoIA.ExtraerRevision(
                respuesta));
    }
}
