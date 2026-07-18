using Xunit;

namespace ControlPCIA.Tests;

public sealed class PlanificadorTareasIATests
{
    [Fact]
    public void Conserva_el_nombre_literal_aunque_el_modelo_omita_el_final()
    {
        const string instruccion =
            "crea un proyecto nuevo en Cubase llamado ControlPCIA IA 20260718 O";

        IReadOnlyList<string> tareas =
            PlanificadorTareasIA.ConservarNombreLiteralExacto(
                instruccion,
                [
                    "crea un proyecto nuevo en Cubase llamado ControlPCIA IA 20260718"
                ]);

        string tarea = Assert.Single(tareas);
        Assert.Contains(
            "ControlPCIA IA 20260718 O",
            tarea,
            StringComparison.Ordinal);
        Assert.Equal(
            "ControlPCIA IA 20260718 O",
            PlanificadorTareasIA.ExtraerNombreLiteralSolicitado(
                instruccion));
    }

    [Theory]
    [InlineData(
        """{"lista":false,"pregunta":"¿Qué nombre quieres poner al proyecto?"}""",
        false,
        "¿Qué nombre quieres poner al proyecto?")]
    [InlineData(
        """{"lista":true,"pregunta":null}""",
        true,
        null)]
    public void Extrae_si_la_ia_necesita_un_dato_del_usuario(
        string respuesta,
        bool lista,
        string? pregunta)
    {
        PreparacionSolicitudControl preparacion = Assert.IsType<
            PreparacionSolicitudControl>(
            PlanificadorTareasIA.ExtraerPreparacion(respuesta));

        Assert.Equal(lista, preparacion.Lista);
        Assert.Equal(pregunta, preparacion.Pregunta);
    }

    [Fact]
    public void Conserva_todas_las_tareas_de_un_plan_json()
    {
        IReadOnlyList<string> tareas =
            PlanificadorTareasIA.ExtraerTareas(
                """
                {"tareas":["Abrir la calculadora","Sumar dos más cinco"]}
                """);

        Assert.Equal(2, tareas.Count);
        Assert.Equal("Abrir la calculadora", tareas[0]);
        Assert.Equal("Sumar dos más cinco", tareas[1]);
    }

    [Fact]
    public void Llama_puede_seleccionar_recetas_por_significado()
    {
        IReadOnlyList<string> conocimientos =
            PlanificadorTareasIA.ExtraerConocimientos(
                """
                {"tareas":["poner Edge delante"],"conocimientos":["ventanas.estado","desconocida"]}
                """);

        Assert.Equal(["ventanas.estado"], conocimientos);
    }

    [Fact]
    public void El_mismo_plan_puede_pedir_una_decision_personal()
    {
        Assert.Equal(
            "¿Qué nombre quieres ponerle?",
            PlanificadorTareasIA.ExtraerPreguntaPlan(
                """
                {"tareas":["crear un proyecto"],"conocimientos":[],"pregunta":"¿Qué nombre quieres ponerle?"}
                """));
        Assert.Null(
            PlanificadorTareasIA.ExtraerPreguntaPlan(
                """
                {"tareas":["abrir Edge"],"conocimientos":["aplicaciones.abrir"],"pregunta":null}
                """));
    }

    [Fact]
    public void Tolera_json_envuelto_pero_no_texto_sin_plan()
    {
        IReadOnlyList<string> tareas =
            PlanificadorTareasIA.ExtraerTareas(
                """
                ```json
                {"tareas":["Abrir Cubase"]}
                ```
                """);

        Assert.Single(tareas);
        Assert.Empty(
            PlanificadorTareasIA.ExtraerTareas(
                "Abrir Cubase"));
    }

    [Fact]
    public void Auditoria_no_acepta_si_hay_tareas_pendientes()
    {
        RevisionTareasControl? revision =
            PlanificadorTareasIA.ExtraerRevision(
                """
                {"completa":false,"pendientes":[2],"motivo":"Sólo se abrió la aplicación."}
                """,
                2);

        Assert.NotNull(revision);
        Assert.False(revision.Completa);
        Assert.Equal([2], revision.Pendientes);
    }

    [Fact]
    public void Auditoria_solo_acepta_sin_pendientes()
    {
        RevisionTareasControl? revision =
            PlanificadorTareasIA.ExtraerRevision(
                """
                {"completa":true,"pendientes":[],"motivo":"Ambas tareas tienen evidencia."}
                """,
                2);

        Assert.NotNull(revision);
        Assert.True(revision.Completa);
        Assert.Empty(revision.Pendientes);
    }

    [Theory]
    [InlineData("¿Qué aplicaciones tengo abiertas?")]
    [InlineData("Dime qué cosas tengo abiertas")]
    [InlineData("Explícame por qué ha fallado")]
    [InlineData(
        "Entonces olvida ese proceso inexistente y dime qué programas con ventana tengo abiertos ahora.")]
    public void Reconoce_peticiones_informativas(string instruccion)
    {
        Assert.True(
            PlanificadorTareasIA.EsPeticionInformativa(instruccion));
    }

    [Theory]
    [InlineData("Abre Cubase")]
    [InlineData("Abre la calculadora y suma dos más cinco")]
    public void No_confunde_ordenes_con_preguntas(string instruccion)
    {
        Assert.False(
            PlanificadorTareasIA.EsPeticionInformativa(instruccion));
    }

    [Theory]
    [InlineData("No se encontró la aplicación. ¿Qué ruta tiene?")]
    [InlineData("Necesito que indiques cuál quieres.")]
    public void Detecta_respuestas_que_requieren_continuar_conversacion(
        string respuesta)
    {
        Assert.True(
            PlanificadorTareasIA.PareceAclaracionPendiente(respuesta));
    }
}
