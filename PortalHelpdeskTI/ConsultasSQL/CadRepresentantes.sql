WITH base
AS (
	SELECT o."CardCode"
		,o."CardName"
		,o."Phone1"
		,o."Phone2"
		,o."LicTradNum"
		,o."E_Mail"
		,o."validFor"
		,
		/* Endereços padrão */
		b."Street" AS "RuaCob"
		,b."StreetNo" AS "NumCob"
		,b."Block" AS "BairroCob"
		,b."City" AS "CidadeCob"
		,b."State" AS "UFCob"
		,s."Street" AS "RuaEnt"
		,s."StreetNo" AS "NumEnt"
		,s."Block" AS "BairroEnt"
		,s."City" AS "CidadeEnt"
		,s."State" AS "UFEnt"
		,
		/* Só dígitos dos telefones */
		REPLACE_REGEXPR('[^0-9]' IN COALESCE(o."Phone1", '') WITH '' OCCURRENCE ALL) AS "Phone1Digits"
		,REPLACE_REGEXPR('[^0-9]' IN COALESCE(o."Phone2", '') WITH '' OCCURRENCE ALL) AS "Phone2Digits"
		,
		/* Escolha do endereço (Cobrança se existir, senão Entrega) */
		COALESCE(NULLIF(b."Street", ''), NULLIF(s."Street", '')) AS "RuaSel"
		,COALESCE(NULLIF(b."StreetNo", ''), NULLIF(s."StreetNo", '')) AS "NumSel"
		,COALESCE(NULLIF(b."Block", ''), NULLIF(s."Block", '')) AS "BairroSel"
		,COALESCE(NULLIF(b."City", ''), NULLIF(s."City", '')) AS "CidadeSel"
		,COALESCE(NULLIF(b."State", ''), NULLIF(s."State", '')) AS "UFSel"
	FROM OCRD o
	LEFT JOIN CRD1 b ON b."CardCode" = o."CardCode"
		AND b."AdresType" = 'B'
		AND b."Address" = o."BillToDef"
	LEFT JOIN CRD1 s ON s."CardCode" = o."CardCode"
		AND s."AdresType" = 'S'
		AND s."Address" = o."ShipToDef"
	WHERE o."CardCode" LIKE 'R%'
	)
SELECT "CardCode"
	,INITCAP("CardName") AS "Nome"
	,
	/* Telefone (DD) 9999-9999 ou (DD) 99999-9999 */
	'(' || RIGHT(LPAD(COALESCE("Phone2Digits", ''), 2, '0'), 2) || ') ' || CASE 
		WHEN LENGTH("Phone1Digits") = 8
			THEN SUBSTRING("Phone1Digits", 1, 4) || '-' || SUBSTRING("Phone1Digits", 5, 4)
		WHEN LENGTH("Phone1Digits") = 9
			THEN SUBSTRING("Phone1Digits", 1, 5) || '-' || SUBSTRING("Phone1Digits", 6, 4)
		WHEN LENGTH("Phone1Digits") > 4
			THEN SUBSTRING("Phone1Digits", 1, LENGTH("Phone1Digits") - 4) || '-' || RIGHT("Phone1Digits", 4)
		ELSE COALESCE("Phone1Digits", '')
		END AS "Telefone"
	,"LicTradNum"
	,"E_Mail"
	,
	/* Endereço único: Rua Nº Num, Bairro - Cidade - UF */
	TRIM(INITCAP(COALESCE("RuaSel", '')) || CASE 
			WHEN COALESCE("NumSel", '') <> ''
				THEN ' Nº ' || INITCAP(TRIM("NumSel"))
			ELSE ''
			END || CASE 
			WHEN COALESCE("BairroSel", '') <> ''
				THEN ', ' || INITCAP(TRIM("BairroSel"))
			ELSE ''
			END || CASE 
			WHEN COALESCE("CidadeSel", '') <> ''
				THEN ' - ' || INITCAP(TRIM("CidadeSel"))
			ELSE ''
			END || CASE 
			WHEN COALESCE("UFSel", '') <> ''
				THEN ' - ' || UPPER(TRIM("UFSel"))
			ELSE ''
			END) AS "Endereço"
	,CASE 
		WHEN "validFor" = 'Y'
			THEN 'Sim'
		ELSE 'Não'
		END AS "Ativo"
FROM base;
