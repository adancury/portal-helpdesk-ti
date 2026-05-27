WITH Movimentacoes AS (
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
      -- filtro opcional de item (já aqui para aliviar carga)
      AND (
            :CodItem IS NULL 
         OR LENGTH(TRIM(:CodItem)) = 0
         OR OINM."ItemCode" LIKE '%' || UPPER(:CodItem) || '%'
      )
),

MovimentacoesAgrupadas AS (
    SELECT
        "ItemCode",
        "DocDate",
        SUM("InQty") AS "TotalEntrada",
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
        SUM("SaldoDia") OVER (
            PARTITION BY "ItemCode" ORDER BY "DocDate"
        ) AS "SaldoAcumulado"
    FROM MovimentacoesAgrupadas
),

ComprometimentosPorData AS (
    SELECT M."ItemCode", M."DocDate", SUM(R."Quantity") AS "QtdComprometida"
    FROM MovimentacoesAcumuladas M
    INNER JOIN RDR1 R ON R."ItemCode" = M."ItemCode"
    INNER JOIN ORDR O ON O."DocEntry" = R."DocEntry"
    LEFT JOIN INV1 I1 ON I1."BaseEntry" = R."DocEntry" AND I1."BaseLine" = R."LineNum"
    LEFT JOIN OINV I ON I."DocEntry" = I1."DocEntry"
    WHERE O."CANCELED" = 'N'
      AND O."DocDate" <= M."DocDate"
      AND (I."DocDate" IS NULL OR I."DocDate" > M."DocDate")
    GROUP BY M."ItemCode", M."DocDate"
),

PedidosCompraAReceber AS (
    SELECT 
        P."ItemCode",
        O."DocDueDate" AS "DocDate",
        SUM(P."OpenQty") AS "QtdAReceber"
    FROM POR1 P
    INNER JOIN OPOR O ON O."DocEntry" = P."DocEntry"
    WHERE O."CANCELED" = 'N'
      AND O."DocStatus" = 'O'
      -- filtro opcional de item também aqui
      AND (
            :CodItem IS NULL 
         OR LENGTH(TRIM(:CodItem)) = 0
         OR P."ItemCode" LIKE '%' || UPPER(:CodItem) || '%'
      )
    GROUP BY P."ItemCode", O."DocDueDate"
)

-- Parte 1: histórico de movimentações
SELECT 
    M."ItemCode",
    OITM."ItemName",
    M."DocDate" AS "Data",
    TO_INTEGER(M."SaldoAcumulado") AS "EstoqueNoDia",
    TO_INTEGER(COALESCE(C."QtdComprometida", 0)) AS "QtdComprometida",
    0 AS "QtdAReceber",
    M."SaldoAcumulado" - COALESCE(C."QtdComprometida", 0) AS "EstoqueDisponivel",
    M."SaldoAcumulado" - COALESCE(C."QtdComprometida", 0) AS "EstoqueFuturo",
    CASE 
        WHEN M."SaldoAcumulado" - COALESCE(C."QtdComprometida", 0) <= 0 THEN 'RUPTURA'
        ELSE 'OK'
    END AS "Status"
FROM MovimentacoesAcumuladas M
LEFT JOIN ComprometimentosPorData C 
       ON C."ItemCode" = M."ItemCode" 
      AND C."DocDate" = M."DocDate"
LEFT JOIN OITM ON OITM."ItemCode" = M."ItemCode"
WHERE
    (
        -- Sem filtro de item -> aplica limite de 12 meses
        ( :CodItem IS NULL OR LENGTH(TRIM(:CodItem)) = 0 )
        AND M."DocDate" >= ADD_MONTHS(CURRENT_DATE, -12)
    )
    OR
    (
        -- Com item informado -> ignora limite de data (traz tudo)
        :CodItem IS NOT NULL AND LENGTH(TRIM(:CodItem)) > 0
    )


UNION ALL

-- Parte 2: pedidos de compra em aberto (entregas futuras)
SELECT 
    PC."ItemCode",
    OITM."ItemName",
    PC."DocDate" AS "Data",
    TO_INTEGER(0) AS "EstoqueNoDia",
    TO_INTEGER(0) AS "QtdComprometida",
    TO_INTEGER(PC."QtdAReceber") AS "QtdAReceber",
    0 AS "EstoqueDisponivel",
    PC."QtdAReceber" AS "EstoqueFuturo",
    'PREVISTO' AS "Status"
FROM PedidosCompraAReceber PC
LEFT JOIN OITM ON OITM."ItemCode" = PC."ItemCode"
WHERE
    OITM."ItmsGrpCod" NOT IN (144, 136, 102, 101, 145)
    AND OITM."validFor" = 'Y'
    AND (
        -- Sem filtro de item -> aplica limite de 12 meses
        (
            ( :CodItem IS NULL OR LENGTH(TRIM(:CodItem)) = 0 )
            AND PC."DocDate" >= ADD_MONTHS(CURRENT_DATE, 12)
        )
        OR
        -- Com item informado -> ignora limite de data
        (
            :CodItem IS NOT NULL AND LENGTH(TRIM(:CodItem)) > 0
        )
    )
ORDER BY "ItemCode", "Data" DESC