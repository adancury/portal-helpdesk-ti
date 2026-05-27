/*******************************************************************************/
-- Consulta: Relatório Dados de Produtos
-- Criador: Emily Garcia - PROJETOS BRW
-- Data da Criação: 20/08/2024
-- Data da Atualização: 16/09/2025
/*******************************************************************************/

WITH
UFD_STR
AS (
	SELECT U."FieldID"
		,U."FldValue"
		,MAX(U."Descr") AS "Descr"
	FROM "UFD1" U
	WHERE U."TableID" = 'OITM'
		AND U."FieldID" IN ('57', '58', '59', '60', '61') 
	GROUP BY U."FieldID"
		,U."FldValue"
	)
	,UDF_MACRO
AS (
	SELECT "FldValue", "Descr"
	FROM UFD_STR
	WHERE "FieldID" = '57'
	)
	,UDF_GRUPO
AS (
	SELECT "FldValue", "Descr"
	FROM UFD_STR
	WHERE "FieldID" = '58'
	)
	,UDF_SUBGRUPO
AS (
	SELECT "FldValue", "Descr"
	FROM UFD_STR
	WHERE "FieldID" = '59'
	)
	,UDF_LINHA
AS (
	SELECT "FldValue", "Descr"
	FROM UFD_STR
	WHERE "FieldID" = '60'
	)
	,UDF_COLECAO
AS (
	SELECT "FldValue", "Descr"
	FROM UFD_STR
	WHERE "FieldID" = '61'
	),
FORA_LINHA_TXT
AS (
	SELECT U."FldValue"
		,MAX(U."Descr") AS "Descr"
	FROM "UFD1" U
	JOIN "CUFD" C ON C."TableID" = U."TableID"
		AND C."FieldID" = U."FieldID"
	WHERE C."TableID" = 'OITM'
		AND C."AliasID" = 'CbxForaLinha'
	GROUP BY U."FldValue"
	),
OBCD_AGG
AS (
	SELECT "ItemCode"
		,MAX("UpdateDate") AS "UpdateDate"
		,MAX("CreateDate") AS "CreateDate"
	FROM "OBCD"
	GROUP BY "ItemCode"
	),
BRW_NCM
AS (
	SELECT CAST("U_IPI_VALUE" AS NVARCHAR(50)) AS k
		,MAX("U_IPI_VALUE") AS "U_IPI_VALUE"
	FROM "@BRW_DADOS_NCM_LINHA"
	GROUP BY CAST("U_IPI_VALUE" AS NVARCHAR(50))
	),
ECOMMERCE
AS (
	SELECT 
		U."FldValue",
		MAX(U."Descr") AS "Descr"
	FROM "UFD1" U
	JOIN "CUFD" C 
	ON C."TableID" = U."TableID"
	AND C."FieldID" = U."FieldID"
	WHERE C."TableID" = 'OITM'
		AND C."AliasID" = 'AS_ECOMMERCE'
	GROUP BY U."FldValue"
	),
STS_ECOMMERCE
AS (
	SELECT 
		U."FldValue",
		MAX(U."Descr") AS "Descr"
	FROM "UFD1" U
	JOIN "CUFD" C 
	ON C."TableID" = U."TableID"
	AND C."FieldID" = U."FieldID"
	WHERE C."TableID" = 'OITM'
		AND C."AliasID" = 'AS_INTEGRADO'
	GROUP BY U."FldValue"
	)
SELECT T0."ItemCode" AS "Código"
	,T0."ItemName" AS "Nome"
	,T5."UpdateDate" AS "Última modificação"
	,
	/* GERAL */
	CASE 
		WHEN T0."validFor" = 'Y'
			THEN 'Sim'
		ELSE 'Não'
		END AS "Ativo"
	,FL."Descr" AS "Fora de Linha"
	,T0."CardCode" AS "Cód.Fornecedor"
	,T6."CardName" AS "Nome Fornecedor"
	,T0."U_LicImport" as "Lic. Import (LI)"
	,T0."U_Dumping" AS "Dumping"
	,CASE 
		WHEN T0."SellItem" = 'Y'
			THEN 'Sim'
		ELSE 'Não'
		END AS "Item de Venda"
	,T10."Descr" AS "Macrocategoria"
	,T7."Descr" AS "Grupo"
	,T8."Descr" AS "Sub-Grupo"
	,T9."Descr" AS "Linha"
	,T11."Descr" AS "Coleção"
	,T0."U_ItemSubstituto" AS "Substituto"
	,CASE 
		WHEN T0."U_MotivoSubstituicao" = 1
			THEN 'Troca de Modelo'
		WHEN T0."U_MotivoSubstituicao" = 2
			THEN 'Nova Coleção'
		WHEN T0."U_MotivoSubstituicao" = 3
			THEN 'Redução Qde'
		ELSE ''
		END AS "Motivo da Substituição"
	,CASE 
		WHEN T0."BuyUnitMsr" = 'Pacote'
			THEN 'PCT'
		WHEN T0."BuyUnitMsr" = 'Caixa'
			THEN 'CX'
		WHEN T0."BuyUnitMsr" = 'Unidade'
			THEN 'UN'
		WHEN T0."BuyUnitMsr" = 'Tubo'
			THEN 'TUB'
		WHEN T0."BuyUnitMsr" = 'Pote'
			THEN 'POT'
		WHEN T0."BuyUnitMsr" = 'Kilo'
			THEN 'KG'
		ELSE ''
		END AS "SKU"
	,CASE 
		WHEN T0."CodeBars" IS NULL
			OR T0."CodeBars" = ''
			THEN 'Manual'
		ELSE 'Preenchido'
		END AS "Grupo de UM"
	,
	/* DADOS EXPO */
	T0."U_TX_UTribEx" AS "Un.Trib. Ext."
	,T0."U_TX_FtTribEx" AS "Fator Trib. Ext."
	,T2."NcmCode" AS "Cód. NCM"
	,T0."U_TX_CodigoCest" AS "Cód. Cest"
	,CONCAT(TO_VARCHAR(TO_DECIMAL(T4."U_IPI_VALUE", 10, 2)), '%') AS "IPI"
	,CASE 
		WHEN T0."ProductSrc" IN (
				'1'
				,'2'
				,'6'
				)
			THEN 'Importado'
		ELSE 'Nacional'
		END AS "Origem"
	,
	/* País de Origem sem ITM10 (usa CountryOrg direto) */
	CASE 
		WHEN T0."CountryOrg" = 'CN'
			THEN 'China'
		WHEN T0."CountryOrg" = 'BR'
			THEN 'Brasil'
		WHEN T0."CountryOrg" = 'IN'
			THEN 'Índia'
		WHEN T0."CountryOrg" = 'PY'
			THEN 'Paraguai'
		ELSE T0."CountryOrg"
		END AS "País de Origem"
	,
	/* DADOS B2B */
	T0."U_NumInmetro"
	,CASE 
		WHEN COALESCE(CAST(T0."U_NumInmetro" AS NVARCHAR(50)), '') IN (
			''
			,'000000/0000'
			,'N/A'
			,'0')
			THEN 'Não'
		ELSE 'Sim'
		END AS "Inmetro"
	,
	/* DEMAIS COLUNAS */
	T0."U_ItemComposicao" AS "Composição"
	,T0."U_MasterPeso" AS "Peso (Master)"
	,T0."U_MasterPesoLiq" AS "Peso Liq.(Master)"
	,T0."U_MasterVolume" AS "Vol. (Master)"
	,T0."U_MasterCodBarras" AS "Cód.Barras (Master)"
	,T0."U_QdeMaster" AS "Qde (Master)"
	,T0."U_MasterComprimento" AS "Comprimento (Master)"
	,T0."U_MasterAltura" AS "Altura (Master)"
	,T0."U_MasterLargura" AS "Largura (Master)"
	,T0."U_InnerPeso" AS "Peso (Inner)"
	,T0."U_InnerPesoLiq" AS "Peso Liq. (Inner)"
	,T0."U_InnerVolume" AS "Vol. (Inner)"
	,T0."U_InnerCodBarras" AS "Cód.Barras (Inner)"
	,T0."U_QdeInner" AS "Qde (Inner)"
	,T0."U_InnerComprimento" AS "Comprimento (Inner)"
	,T0."U_InnerAltura" AS "Altura(Inner)"
	,T0."U_InnerLargura" AS "Largura (Inner)"
	,T0."U_EmbPeso" AS "Peso (Embalagem)"
	,T0."U_EmbPesoLiq" AS "Peso Liq. (Embalagem)"
	,T0."U_EmbVolume" AS "Vol. (Embalagem)"
	,T0."U_EmbCodBarras" AS "Cód.Barras(Embalagem)"
	,T0."U_EmbComprimento" AS "Comprimento (Embalagem)"
	,T0."U_EmbAltura" AS "Altura (Embalagem)"
	,T0."U_EmbLargura" AS "Altura (Embalagem)"
	,T0."U_ProdCodBarras" AS "Cód. Barras (Prod)"
	,T0."U_QdeUniVendas" AS "Qde (Prod)"
	,T0."U_ProdComprimento" AS "Comprimento (Prod)"
	,T0."U_ProdAltura" AS "Altura (Prod)"
	,T0."U_ProdLargura" AS "Largura (Prod)"
	,
	/* INFOS CATALOGO */
	T0."U_LocalCatalogo" AS "Local Catálogo"
	,T0."U_ItemCaract01" AS "Característica 1"
	,T0."U_ItemCaract02" AS "Característica 2"
	,T0."U_ItemCaract03" AS "Característica 3"
	,T0."U_ItemCaract04" AS "Característica 4"
	,
	/* DADOS QUALIDADE */
	CASE 
		WHEN T0."U_MatUniVendas" = 0
			THEN 'Papelão'
		WHEN T0."U_MatUniVendas" = 1
			THEN 'Plástico'
		WHEN T0."U_MatUniVendas" = 2
			THEN 'N/A'
		ELSE ''
		END AS "Material Un. Venda"
	,CASE 
		WHEN T0."U_MatInner" = 0
			THEN 'Papelão'
		WHEN T0."U_MatInner" = 1
			THEN 'Plástico'
		WHEN T0."U_MatInner" = 2
			THEN 'N/A'
		ELSE ''
		END AS "Material Inner"
	,CASE 
		WHEN T0."U_MatMaster" = 0
			THEN 'Papelão'
		WHEN T0."U_MatMaster" = 1
			THEN 'Plástico'
		WHEN T0."U_MatMaster" = 2
			THEN 'N/A'
		ELSE ''
		END AS "Material Master"
	,
	/* DADOS PALETIZAÇÃO */
	T0."U_Pallet_Lastro" AS "Lastro (Paletização)"
	,T0."U_Pallet_Altura" AS "Altura (Paletização)"
	,T0."U_MasterPeso" AS "Peso Master (Paletização)"
	,T0."U_Pallet_Qde" AS "Quantidade Palet (Paletização)"
	,T0."U_QdeMaster" AS "Quantidade de Master (Paletização)"
	,T0."U_Pallet_Peso" AS "Peso Palet (Paletização)"
	,T0."U_MasterPorPallet" AS "Master p/Palet"
	,T5."CreateDate" AS "Data de Criação"
	,ECO."Descr" AS "Integrar B2B"
	,STS_ECO."Descr" AS "Integrado (B2B)?"
FROM "OITM" T0
LEFT JOIN "ONCM" T2 ON T0."NCMCode" = T2."AbsEntry"
-- @BRW_DADOS_NCM_LINHA colapsada e comparando como NVARCHAR
LEFT JOIN BRW_NCM T4 ON CAST(T0."U_Valor_IPI" AS NVARCHAR(50)) = T4.k
LEFT JOIN OBCD_AGG T5 ON T0."ItemCode" = T5."ItemCode"
LEFT JOIN "OCRD" T6 ON T0."CardCode" = T6."CardCode"
-- UDFs via JOIN por TEXTO (FldValue) ⇄ CAST(NVARCHAR)
LEFT JOIN FORA_LINHA_TXT FL ON FL."FldValue" = CAST(T0."U_CbxForaLinha" AS NVARCHAR(50))
LEFT JOIN ECOMMERCE ECO ON ECO."FldValue" = CAST(T0."U_AS_ECOMMERCE" AS NVARCHAR(50))/*STS_ECOMMERCE*/
LEFT JOIN STS_ECOMMERCE STS_ECO ON STS_ECO."FldValue" = CAST(T0."U_AS_INTEGRADO" AS NVARCHAR(50))/*STS_ECOMMERCE*/
LEFT JOIN UDF_GRUPO T7 ON T7."FldValue" = CAST(T0."U_ItemGrupo" AS NVARCHAR(50))
LEFT JOIN UDF_SUBGRUPO T8 ON T8."FldValue" = CAST(T0."U_ItemSubGrupo" AS NVARCHAR(50))
LEFT JOIN UDF_LINHA T9 ON T9."FldValue" = CAST(T0."U_ItemLinha" AS NVARCHAR(50))
LEFT JOIN UDF_MACRO T10 ON T10."FldValue" = CAST(T0."U_ItemMacro" AS NVARCHAR(50))
LEFT JOIN UDF_COLECAO T11 ON T11."FldValue" = CAST(T0."U_ItemColecao" AS NVARCHAR(50))
WHERE T0."ItemCode" NOT IN ('3229','ITNOVO')
