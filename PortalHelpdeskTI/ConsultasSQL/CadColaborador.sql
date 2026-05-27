SELECT 
    -- IDENTIFICAÇÃO BÁSICA
    T0."empID"              AS "ID Interno",
    T0."ExtEmpNo"           AS "Matrícula",
    -- INITCAP(T0."lastName")     AS "Sobrenome",
    -- INITCAP(T0."firstName")    AS "Nome",
    -- INITCAP(T0."middleName")   AS "Nome do Meio",
    INITCAP(
        T0."firstName" || ' ' || IFNULL(T0."middleName", '') || ' ' || T0."lastName"
    )                       AS "Nome Completo",

    -- DADOS PESSOAIS
    T0."sex"                AS "Sexo Código",
    CASE T0."sex"
        WHEN 'M' THEN 'Masculino'
        WHEN 'F' THEN 'Feminino'
        ELSE 'Não Informado'
    END                     AS "Sexo",
    T0."birthDate"          AS "Data Nascimento",
    FLOOR(DAYS_BETWEEN(T0."birthDate", CURRENT_DATE) / 365.25) AS "Idade (Anos)",
    T0."brthCountr"         AS "País Nasc. Código",
    INITCAP(CnBirth."Name") AS "País Nascimento",
	CASE T0."martStatus"
        WHEN 'M' THEN 'Casado'
        WHEN 'F' THEN 'Solteiro'
        ELSE 'Não Informado'
    END                     AS "Estado Civil Código",
    T0."nChildren"          AS "Qtd Filhos",
    T0."citizenshp"         AS "Cidadania Código",
    INITCAP(CnCit."Name")   AS "País Cidadania",

    -- DADOS CORPORATIVOS
    INITCAP(T0."jobTitle")  AS "Cargo",
    T0."dept"               AS "Depto Código",
    INITCAP(D."Name")       AS "Departamento",
    T0."manager"            AS "Gestor ID",
    INITCAP(M."firstName" || ' ' || M."lastName") AS "Gestor",
    T0."salesPrson"         AS "Vendedor Código",
    INITCAP(S."SlpName")    AS "Vendedor / Representante",
    T0."CostCenter"         AS "Centro Custo",
    INITCAP(CC."PrcName")   AS "Nome Centro Custo",

    T0."startDate"          AS "Data Admissão",
    FLOOR(DAYS_BETWEEN(T0."startDate", CURRENT_DATE) / 365.25) AS "Tempo De Casa (Anos)",

    -- SITUAÇÃO / STATUS
    T0."status"             AS "Status Código",
    T0."termDate"           AS "Data Desligamento",
    INITCAP(TR."name") AS "Motivo Desligamento",

    -- CONTATOS
    T0."officeTel"          AS "Telefone Corporativo",
    T0."mobile"             AS "Celular",
    T0."homeTel"            AS "Telefone Residencial",
    T0."email"              AS "E-mail",

    -- ENDEREÇO RESIDENCIAL
    INITCAP(T0."homeStreet") AS "Endereço",
    T0."StreetNoH"           AS "Número",
    INITCAP(T0."homeBlock")  AS "Bairro",
    T0."homeZip"             AS "CEP",
    INITCAP(T0."homeCity")   AS "Cidade",
    INITCAP(T0."homeCounty") AS "Município (County)",
    T0."homeCountr"          AS "País Código",
    INITCAP(CnHome."Name")   AS "País",
    T0."homeState"           AS "UF Código",
    INITCAP(StHome."Name")   AS "UF",

        -- FÉRIAS / TRILHAS (COM TRATAMENTO PARA DESLIGADOS)
    CASE 
        WHEN T0."status" = 2 THEN 
            'Desligado em ' || TO_VARCHAR(T0."termDate", 'DD/MM/YYYY')
        ELSE TO_VARCHAR(T0."U_DtFeriasProgramada", 'DD/MM/YYYY')
    END AS "Férias Programadas",

    CASE 
        WHEN T0."status" = 2 THEN 
            'Desligado em ' || TO_VARCHAR(T0."termDate", 'DD/MM/YYYY')
        ELSE TO_VARCHAR(T0."U_DtFeriasVencimento", 'DD/MM/YYYY')
    END AS "Férias Vencimento",

    CASE 
        WHEN T0."status" = 2 THEN 
            'Desligado em ' || TO_VARCHAR(T0."termDate", 'DD/MM/YYYY')
        ELSE TO_VARCHAR(T0."U_DtFeriasLimite", 'DD/MM/YYYY')
    END AS "Férias Limite",

    CASE 
        WHEN T0."status" = 2 THEN 
            'Desligado em ' || TO_VARCHAR(T0."termDate", 'DD/MM/YYYY')
        ELSE TO_VARCHAR(T0."U_SaldoFerias")
    END AS "Saldo De Férias (Dias)",

    CASE 
        WHEN T0."status" = 2 THEN 
            'Desligado em ' || TO_VARCHAR(T0."termDate", 'DD/MM/YYYY')
        ELSE
            CASE 
                WHEN T0."U_DtFeriasVencimento" IS NULL THEN NULL
                ELSE TO_VARCHAR(DAYS_BETWEEN(CURRENT_DATE, T0."U_DtFeriasVencimento"))
            END
    END AS "Dias P/ Vencimento Férias",

    CASE 
        WHEN T0."status" = 2 THEN 
            'Desligado em ' || TO_VARCHAR(T0."termDate", 'DD/MM/YYYY')
        ELSE
            CASE 
                WHEN T0."U_DtFeriasLimite" IS NULL THEN NULL
                ELSE TO_VARCHAR(DAYS_BETWEEN(CURRENT_DATE, T0."U_DtFeriasLimite"))
            END
    END AS "Dias P/ Limite Férias",

    CASE
        WHEN T0."status" = 2 THEN 
            'Desligado em ' || TO_VARCHAR(T0."termDate", 'DD/MM/YYYY')
        ELSE TO_VARCHAR(T0."U_DtUltimaTrilha", 'DD/MM/YYYY')
    END AS "Dt Última Trilha",

    CASE
        WHEN T0."status" = 2 THEN 
            'Desligado em ' || TO_VARCHAR(T0."termDate", 'DD/MM/YYYY')
        ELSE TO_VARCHAR(T0."U_DtProximaTrilha", 'DD/MM/YYYY')
    END AS "Dt Próxima Trilha",

    -- ÚLTIMA FORMAÇÃO (HEM2 / OHED)
    F."fromDate"                    AS "Formação De",
    F."toDate"                      AS "Formação Até",
    INITCAP(T2."name")             AS "Tipo De Formação",
    INITCAP(F."institute")         AS "Instituição",
    INITCAP(F."major")             AS "Curso",
    INITCAP(F."diploma")           AS "Diploma / Certificação",

    -- ÚLTIMA AVALIAÇÃO (HEM3)
    A."date"                        AS "Data Avaliação",
    INITCAP(A."reviewDesc")        AS "Descrição Avaliação",
    A."grade"                      AS "Progresso Salário",
    INITCAP(A."remarks")           AS "Observações Avaliação",

    -- OUTROS CAMPOS
    T0."picture"                    AS "Foto (Caminho/Id)"

FROM OHEM T0

    ----------------------------------------------------------------
    -- SUBSELECT: ÚLTIMA FORMAÇÃO POR COLABORADOR (HEM2)
    ----------------------------------------------------------------
    LEFT JOIN (
        SELECT H2.*
        FROM HEM2 H2
        INNER JOIN (
            SELECT 
                "empID",
                MAX(COALESCE("toDate", "fromDate")) AS "MaxDate"
            FROM HEM2
            GROUP BY "empID"
        ) X
            ON X."empID" = H2."empID"
           AND COALESCE(H2."toDate", H2."fromDate") = X."MaxDate"
    ) F
        ON F."empID" = T0."empID"

    LEFT JOIN OHED T2
        ON F."type" = T2."edType"

    ----------------------------------------------------------------
    -- SUBSELECT: ÚLTIMA AVALIAÇÃO POR COLABORADOR (HEM3)
    ----------------------------------------------------------------
    LEFT JOIN (
        SELECT H3.*
        FROM HEM3 H3
        INNER JOIN (
            SELECT 
                "empID",
                MAX("date") AS "MaxDate"
            FROM HEM3
            GROUP BY "empID"
        ) Y
            ON Y."empID" = H3."empID"
           AND H3."date" = Y."MaxDate"
    ) A
        ON A."empID" = T0."empID"

    ----------------------------------------------------------------
    -- DEMAIS TABELAS RELACIONADAS
    ----------------------------------------------------------------
    -- DEPARTAMENTO
    LEFT JOIN OUDP D
        ON T0."dept" = D."Code"

    -- GESTOR (OUTRO EMPREGADO NA OHEM)
    LEFT JOIN OHEM M
        ON T0."manager" = M."empID"

    -- VENDEDOR / REPRESENTANTE
    LEFT JOIN OSLP S
        ON T0."salesPrson" = S."SlpCode"

    -- CENTRO DE CUSTO
    LEFT JOIN OPRC CC
        ON T0."CostCenter" = CC."PrcCode"

    -- PAÍSES / ESTADOS
    LEFT JOIN OCRY CnHome
        ON T0."homeCountr" = CnHome."Code"
    LEFT JOIN OCRY CnBirth
        ON T0."brthCountr" = CnBirth."Code"
    LEFT JOIN OCRY CnCit
        ON T0."citizenshp" = CnCit."Code"
    LEFT JOIN OCST StHome
        ON T0."homeState" = StHome."Code"
       AND T0."homeCountr" = StHome."Country"
	LEFT JOIN OHTR TR
		ON TR."reasonID" = T0."termReason"
WHERE T0."dept" NOT IN (11)
ORDER BY T0."ExtEmpNo"