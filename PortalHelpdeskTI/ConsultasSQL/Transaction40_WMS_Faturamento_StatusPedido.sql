-- ======================================================================
-- TRANSACTION No40 - Envio de Nota para WMS + Status Pedido Faturado
-- TN - Nota Fiscal Saida (OINV) - object_type = '13'
-- Regra:
--   1) Enviar ao WMS somente NF de saida ativa
--   2) Nao enviar notas canceladas nem notas de cancelamento
--   3) Ao gerar NF, enfileirar atualizacao do pedido de venda para Status 10
-- ======================================================================
IF :object_type = '13' AND :transaction_type = 'A' THEN

    DECLARE v_docEntry INT;
    DECLARE v_docNum INT;
    DECLARE v_numOrdSaida INT;
    DECLARE v_numNf INT;
    DECLARE v_valorNF DECIMAL(19,6);
    DECLARE v_serieNF NVARCHAR(50);
    DECLARE v_dataFat NVARCHAR(10);
    DECLARE v_chaveNf NVARCHAR(60);
    DECLARE v_numeroDoc INT;
    DECLARE v_code NVARCHAR(50);
    DECLARE v_code_status NVARCHAR(50);

    v_docEntry := TO_INT(:list_of_cols_val_tab_del);

    IF EXISTS (
        SELECT 1
        FROM OINV T0
        WHERE T0."DocEntry" = :v_docEntry
          AND IFNULL(T0."CANCELED", 'N') = 'N'
    ) THEN

        SELECT
            T0."DocNum",
            IFNULL(T0."Serial", 0),
            T0."DocTotal",
            IFNULL(T0."SeriesStr", ''),
            TO_NVARCHAR(T0."DocDate", 'DD/MM/YYYY'),
            IFNULL(NULLIF(MAX(P."KeyNfe"), ''), IFNULL(NULLIF(T0."U_ChaveAcesso", ''), '')),
            T0."DocNum"
        INTO
            v_docNum,
            v_numNf,
            v_valorNF,
            v_serieNF,
            v_dataFat,
            v_chaveNf,
            v_numeroDoc
        FROM OINV T0
        LEFT JOIN "DBInvOne"."Process" P
               ON P."DocType" = 13
              AND P."DocEntry" = T0."DocEntry"
        WHERE T0."DocEntry" = :v_docEntry
          AND IFNULL(T0."CANCELED", 'N') = 'N'
        GROUP BY
            T0."DocNum",
            T0."Serial",
            T0."DocTotal",
            T0."SeriesStr",
            T0."DocDate";

        SELECT IFNULL(MIN(T1."BaseEntry"), 0)
        INTO v_numOrdSaida
        FROM INV1 T1
        WHERE T1."DocEntry" = :v_docEntry
          AND T1."BaseType" = 17;

        IF NOT EXISTS (
            SELECT 1
            FROM "SBO_BRW_PRD"."@BRW_WMS_FILA_FAT" F
            WHERE F."U_DocEntry" = :v_docEntry
              AND F."U_TipoEvento" = 'EMISSAO'
        ) THEN

            v_code := TO_NVARCHAR(CURRENT_TIMESTAMP, 'YYYYMMDDHH24MISSFF3');

            INSERT INTO "SBO_BRW_PRD"."@BRW_WMS_FILA_FAT"
            (
                "Code",
                "Name",
                "U_DocEntry",
                "U_DocNum",
                "U_ObjType",
                "U_TipoEvento",
                "U_Status",
                "U_NumOrdSaida",
                "U_NumNf",
                "U_ValorNF",
                "U_SerieNF",
                "U_DataFaturamento",
                "U_ChaveNf",
                "U_NumeroDoc",
                "U_Tentativas",
                "U_DataInclusao"
            )
            VALUES
            (
                :v_code,
                :v_code,
                :v_docEntry,
                :v_docNum,
                '13',
                'EMISSAO',
                'PENDENTE',
                :v_numOrdSaida,
                :v_numNf,
                :v_valorNF,
                :v_serieNF,
                :v_dataFat,
                :v_chaveNf,
                :v_numeroDoc,
                0,
                CURRENT_TIMESTAMP
            );

        END IF;

        IF :v_numOrdSaida > 0
           AND NOT EXISTS (
               SELECT 1
               FROM "SBO_BRW_PRD"."@BRW_WMS_FILA_FAT" F
               WHERE F."U_DocEntry" = :v_docEntry
                 AND F."U_TipoEvento" = 'STATUS_PEDIDO_FATURADO'
           ) THEN

            v_code_status := TO_NVARCHAR(CURRENT_TIMESTAMP, 'YYYYMMDDHH24MISSFF3') || 'S';

            INSERT INTO "SBO_BRW_PRD"."@BRW_WMS_FILA_FAT"
            (
                "Code",
                "Name",
                "U_DocEntry",
                "U_DocNum",
                "U_ObjType",
                "U_TipoEvento",
                "U_Status",
                "U_NumOrdSaida",
                "U_NumNf",
                "U_ValorNF",
                "U_SerieNF",
                "U_DataFaturamento",
                "U_ChaveNf",
                "U_NumeroDoc",
                "U_Tentativas",
                "U_DataInclusao"
            )
            VALUES
            (
                :v_code_status,
                :v_code_status,
                :v_docEntry,
                :v_docNum,
                '13',
                'STATUS_PEDIDO_FATURADO',
                'PENDENTE',
                :v_numOrdSaida,
                :v_numNf,
                :v_valorNF,
                :v_serieNF,
                :v_dataFat,
                :v_chaveNf,
                :v_numeroDoc,
                0,
                CURRENT_TIMESTAMP
            );

        END IF;

    END IF;

END IF;
