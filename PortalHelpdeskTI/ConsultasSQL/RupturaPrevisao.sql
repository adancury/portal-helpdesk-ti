WITH
/* =========================
   HISTÓRICO BASE (para achar estoque atual)
   ========================= */
Movimentacoes AS (
    SELECT 
        OINM."ItemCode",
        OINM."DocDate",
        OINM."InQty",
        OINM."OutQty",
        (OINM."InQty" - OINM."OutQty") AS "SaldoDia"
    FROM OINM
    INNER JOIN OITM ON OINM."ItemCode" = OITM."ItemCode"
    WHERE OITM."ItmsGrpCod" NOT IN (144, 136, 102, 101, 145)
      AND OITM."validFor" = 'Y'
      AND OINM."ItemCode" IS NOT NULL
),
MovimentacoesAgrupadas AS (
    SELECT
        "ItemCode",
        "DocDate",
        SUM("InQty")  AS "TotalEntrada",
        SUM("OutQty") AS "TotalSaida",
        SUM("InQty") - SUM("OutQty") AS "SaldoDia"
    FROM Movimentacoes
    GROUP BY "ItemCode", "DocDate"
),
MovimentacoesAcumuladas AS (
    SELECT
        "ItemCode",
        "DocDate",
        "SaldoDia",
        SUM("SaldoDia") OVER (PARTITION BY "ItemCode" ORDER BY "DocDate") AS "SaldoAcumulado"
    FROM MovimentacoesAgrupadas
),

/* =========================
   ESTOQUE ATUAL (até hoje)
   ========================= */
EstoqueAtualPorItem AS (
    SELECT X."ItemCode", X."SaldoAcumulado" AS "EstoqueAtual"
    FROM (
        SELECT 
            M."ItemCode", 
            M."SaldoAcumulado",
            ROW_NUMBER() OVER (PARTITION BY M."ItemCode" ORDER BY M."DocDate" DESC) AS rn
        FROM MovimentacoesAcumuladas M
        WHERE M."DocDate" <= CURRENT_DATE
    ) X
    WHERE X.rn = 1
),

/* =========================
   FUTURO: VENDAS DO DIA e PC DO DIA
   ========================= */
VendasFuturasDia AS (
    SELECT
        R."ItemCode",
        O."DocDueDate" AS "DocDate",
        SUM(R."OpenQty") AS "QtdVenderDia"
    FROM RDR1 R
    JOIN ORDR O ON O."DocEntry" = R."DocEntry"
    WHERE O."CANCELED" = 'N'
      AND O."DocStatus" = 'O'
      AND O."DocDueDate" > CURRENT_DATE
    GROUP BY R."ItemCode", O."DocDueDate"
),
ComprasFuturasDia AS (
    SELECT
        P."ItemCode",
        O."DocDueDate" AS "DocDate",
        SUM(P."OpenQty") AS "QtdAReceberDia"
    FROM POR1 P
    JOIN OPOR O ON O."DocEntry" = P."DocEntry"
    WHERE O."CANCELED" = 'N'
      AND O."DocStatus" = 'O'
      AND O."DocDueDate" > CURRENT_DATE
    GROUP BY P."ItemCode", O."DocDueDate"
),

/* =========================
   CALENDÁRIO ÚNICO DE DATAS FUTURAS (evita duplicidade)
   ========================= */
CalendarioFuturo AS (
    SELECT DISTINCT "ItemCode", "DocDate" FROM VendasFuturasDia
    UNION
    SELECT DISTINCT "ItemCode", "DocDate" FROM ComprasFuturasDia
),

/* =========================
   MÉTRICAS FUTURAS POR DATA (1 linha por data)
   ========================= */
FuturoPorData AS (
    SELECT
        C."ItemCode",
        C."DocDate",
        COALESCE(CD."QtdAReceberDia", 0) AS "QtdAReceberDia",
        COALESCE(VF."QtdVenderDia", 0)   AS "QtdVenderDia"
    FROM CalendarioFuturo C
    LEFT JOIN ComprasFuturasDia CD
           ON CD."ItemCode" = C."ItemCode" AND CD."DocDate" = C."DocDate"
    LEFT JOIN VendasFuturasDia VF
           ON VF."ItemCode" = C."ItemCode" AND VF."DocDate" = C."DocDate"
),

/* =========================
   ACUMULADOS:
   - PC acumulado até o dia anterior (para formar estoque de abertura)
   - Vendas acumuladas até a data (comprometido)
   ========================= */
Acumulados AS (
    SELECT
        F."ItemCode",
        F."DocDate",
        F."QtdAReceberDia",
        F."QtdVenderDia",
        /* Compras acumuladas ATÉ o dia ANTERIOR */
        COALESCE(
            SUM(F."QtdAReceberDia") OVER (
                PARTITION BY F."ItemCode"
                ORDER BY F."DocDate"
                ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING
            ), 0
        ) AS "PcAcumAteOntem",
        /* Vendas acumuladas ATÉ a data */
        COALESCE(
            SUM(F."QtdVenderDia") OVER (
                PARTITION BY F."ItemCode"
                ORDER BY F."DocDate"
                ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
            ), 0
        ) AS "VendasAcumAteData"
    FROM FuturoPorData F
),

/* =========================
   ESTOQUE PROJETADO DO DIA
   ========================= */
Projecao AS (
    SELECT
        A."ItemCode",
        A."DocDate",
        (COALESCE(EA."EstoqueAtual", OITM."OnHand", 0) + A."PcAcumAteOntem") AS "EstoqueNoDia",
        A."QtdAReceberDia",
        A."VendasAcumAteData"
    FROM Acumulados A
    LEFT JOIN EstoqueAtualPorItem EA ON EA."ItemCode" = A."ItemCode"
    LEFT JOIN OITM ON OITM."ItemCode" = A."ItemCode"
)

/* =========================
   SAÍDA: SOMENTE PREVISÃO (sem histórico) e 1 linha por data
   ========================= */
SELECT
    'PREVISAO' AS "TipoRegistro",
    P."ItemCode",
    OITM."ItemName" AS "ItemName",
    CASE CAST(OITM."U_CbxForaLinha" AS NVARCHAR(10))
        WHEN '0' THEN 'Não Promocionar'
        WHEN '1' THEN 'Não'
        WHEN '2' THEN 'Sim Promocionar'
        ELSE 'Não informado'
    END AS "ForaLinhaStatus",
    P."DocDate"     AS "Data",
    TO_INTEGER(P."EstoqueNoDia") AS "EstoqueNoDia",
    TO_INTEGER(P."VendasAcumAteData") AS "QtdComprometida",
    TO_INTEGER(P."QtdAReceberDia") AS "QtdAReceber",
    ( P."EstoqueNoDia" + P."QtdAReceberDia" - P."VendasAcumAteData" ) AS "EstoqueDisponivel",
    CASE 
        WHEN (P."EstoqueNoDia" + P."QtdAReceberDia" - P."VendasAcumAteData") <= 0 
            THEN 'RUPTURA' 
        ELSE 'OK' 
    END AS "Status"
FROM Projecao P
LEFT JOIN OITM ON OITM."ItemCode" = P."ItemCode"
WHERE
    (
        (? IS NULL OR LENGTH(TRIM(?)) = 0)
        OR P."ItemCode" LIKE '%' || UPPER(?) || '%'
    )
    AND OITM."ItmsGrpCod" NOT IN (144, 136, 102, 101, 145)
    AND OITM."validFor" = 'Y'
ORDER BY P."ItemCode", P."DocDate" DESC