namespace CenoteVivo
{
    /// <summary>
    /// Todos los textos de la app en un solo lugar para facilitar su edición o traducción.
    /// </summary>
    public static class TextosCenote
    {
        public const string Titulo = "CENOTE VIVO";
        public const string Subtitulo = "El agua que no ves, también se contamina";

        public static readonly string[] FrasesImpacto =
        {
            "En la Península de Yucatán, casi toda el agua que bebemos viene del subsuelo.",
            "Lo que tiras en la superficie puede llegar al cenote en horas, no en años.",
            "El suelo calizo es como una esponja: deja pasar el agua… y también la basura.",
            "Un cenote limpio hoy es el agua potable de mañana.",
            "No hay ríos en la superficie: los cenotes son las ventanas a nuestro río subterráneo.",
            "Cuidar el subsuelo es cuidar la vida que no vemos.",
        };

        public const string BannerGuia =
            "Explora el cenote y descubre cómo lo que hacemos en la superficie llega al agua subterránea. " +
            "Usa el control de contaminación para ver el impacto.";

        public const string TituloInfo = "¿Por qué se contamina un cenote?";

        public static readonly (string titulo, string cuerpo)[] SeccionesInfo =
        {
            ("Un acuífero sin filtro",
             "La Península de Yucatán está formada por roca caliza muy porosa y fracturada. El agua de lluvia " +
             "no corre por ríos: se infiltra rápidamente al subsuelo y forma un enorme sistema de ríos y cavernas " +
             "subterráneas conectadas entre sí. Los cenotes son las aberturas de ese sistema. Como el suelo es " +
             "delgado y la roca deja pasar el agua casi sin filtrarla, cualquier contaminante en la superficie " +
             "puede llegar al acuífero en muy poco tiempo."),

            ("Filtración de residuos",
             "Fosas sépticas mal construidas, aguas residuales sin tratar, lixiviados de basureros a cielo abierto, " +
             "desechos de granjas porcinas, combustibles y el exceso de fertilizantes y plaguicidas se infiltran por " +
             "las grietas de la roca. Al llegar al agua subterránea se dispersan por kilómetros siguiendo las " +
             "corrientes, por lo que un foco de contaminación puede afectar cenotes y pozos muy lejanos."),

            ("Pérdida de biodiversidad",
             "Los cenotes albergan especies únicas en el mundo, como peces y crustáceos ciegos adaptados a la " +
             "oscuridad. El exceso de nutrientes provoca la proliferación de algas que consumen el oxígeno del agua " +
             "(eutrofización); los metales y químicos son tóxicos para la fauna. Cuando el agua se enturbia y pierde " +
             "oxígeno, estas especies, que no pueden migrar a otro lugar, desaparecen."),

            ("Riesgos para el consumo humano",
             "Gran parte de la población de la región obtiene su agua de pozos que extraen del mismo acuífero. " +
             "El agua contaminada puede contener bacterias fecales (como E. coli), nitratos, plaguicidas, " +
             "hidrocarburos y microplásticos, que causan enfermedades gastrointestinales, afectaciones en bebés " +
             "y riesgos a largo plazo para la salud. Limpiar un acuífero contaminado es extremadamente difícil y costoso."),

            ("¿Qué puedes hacer?",
             "• No tires basura, aceites ni químicos al suelo, al drenaje o a los cenotes.\n" +
             "• Usa biodigestores o fosas sépticas bien selladas y dales mantenimiento.\n" +
             "• Reduce el uso de fertilizantes y plaguicidas; prefiere productos biodegradables.\n" +
             "• Al nadar en un cenote, báñate antes y no uses bloqueador ni repelente.\n" +
             "• Recicla y separa tus residuos, en especial pilas y electrónicos.\n" +
             "• Comparte lo que aprendiste: el acuífero es de todos."),
        };

        public static readonly string[] DescripcionesVistas =
        {
            "Vista general: así se ve el cenote desde la superficie.",
            "Nivel del agua: observa la transparencia y el color del agua.",
            "Corte del subsuelo: mira cómo la contaminación se infiltra a través de la roca.",
        };

        // Etiquetas de la escena AR: (título, explicación corta de la tarjeta).
        public static readonly (string titulo, string texto)[] EtiquetasAR =
        {
            ("Roca caliza",
             "Yucatán está hecho de roca caliza, porosa como una esponja. La lluvia se filtra por sus grietas " +
             "hasta el agua subterránea… y la contaminación también pasa casi sin filtrarse. " +
             "Toca «Ver subsuelo» para ver las capas de roca."),
            ("Agua subterránea",
             "El agua del cenote es parte de un gran río bajo la tierra: el Gran Acuífero Maya. " +
             "Es la única fuente de agua potable de Mérida y de todo Yucatán. Lo que la ensucia aquí, " +
             "llega a los pozos de los que bebemos."),
            ("Raíces",
             "Las raíces del álamo bajan por las paredes hasta alcanzar el agua. Junto con helechos, lianas y musgos, " +
             "forman la vida verde del cenote. Cuando el agua se contamina, esta vegetación se marchita."),
            ("Contaminación",
             "Fosas sépticas, granjas de cerdos, basureros y agroquímicos se infiltran por la roca y llegan " +
             "al cenote en horas, no en años. Mueve el control de contaminación y mira cómo el agua se enturbia."),
        };
    }
}
