import pyodbc
import pandas as pd
from prophet import Prophet
from datetime import datetime
import sys
import traceback


# ==============================
# CONFIGURACOES
# ==============================

HANA_CONN_STR = (
    "Driver={HDBODBC};"
    "ServerNode=10.123.46.7:30015;"
    "UID=SYSTEM;"
    "PWD={*EmMx5!20LbgGi#};"
    "CURRENTSCHEMA=SBO_BRW_PRD"
)

# Quantos dias para frente queremos prever
DIAS_FORECAST = 90

# Quantos dias usar de historico. A consulta ja limita em 730 dias.
MIN_PONTOS_HISTORICO = 60

# Tamanho do lote para INSERT via ODBC.
# Mantem a transacao menor e evita que o driver segure centenas de milhares de linhas em memoria.
BATCH_INSERT_SIZE = 5000


def emitir_progresso(percentual, mensagem):
    percentual = max(0, min(100, int(percentual)))
    print(f"PROGRESS:{percentual}:{mensagem}", flush=True)


# ==============================
# AJUDANTES
# ==============================

def obter_historico_vendas(conn):
    sql = """
    SELECT
        L."ItemCode"       AS "ItemCode",
        H."DocDate"        AS "Data",
        SUM(L."Quantity")  AS "QtdVendida"
    FROM "INV1" L
    JOIN "OINV" H ON H."DocEntry" = L."DocEntry"
    WHERE H."CANCELED" = 'N'
      AND H."DocDate" >= ADD_DAYS(CURRENT_DATE, -730)
      AND H."CardCode" NOT IN ('C006102', 'C020604')
    GROUP BY
        L."ItemCode",
        H."DocDate"
    ORDER BY
        L."ItemCode",
        H."DocDate";
    """
    df = pd.read_sql(sql, conn)
    df["Data"] = pd.to_datetime(df["Data"]).dt.date
    df["QtdVendida"] = df["QtdVendida"].astype(float)
    return df


def limpar_previsoes_antigas(conn):
    cur = conn.cursor()
    cur.execute('DELETE FROM "Z_RUPTURA_PREV_CONSUMO"')
    conn.commit()


def salvar_previsoes(conn, df_prev):
    if df_prev.empty:
        return

    cur = conn.cursor()
    cur.fast_executemany = True

    rows = [
        (
            row.ItemCode,
            row.Data,
            float(row.QtdPrevistaDia),
            row.GeradoEm,
        )
        for row in df_prev.itertuples(index=False)
    ]

    sql_insert = """
    INSERT INTO "Z_RUPTURA_PREV_CONSUMO"
        ("ItemCode", "Data", "QtdPrevistaDia", "GeradoEm")
    VALUES (?, ?, ?, ?)
    """

    total = len(rows)
    for inicio in range(0, total, BATCH_INSERT_SIZE):
        fim = min(inicio + BATCH_INSERT_SIZE, total)
        cur.executemany(sql_insert, rows[inicio:fim])
        conn.commit()
        percentual = 88 + (fim / max(total, 1)) * 10
        emitir_progresso(percentual, f"Salvando previsoes {fim}/{total}")
        print(f"Salvas {fim}/{total} linhas de previsao...")


def prever_por_item(df_item):
    df_fit = df_item.rename(columns={"Data": "ds", "QtdVendida": "y"}).copy()
    df_fit["ds"] = pd.to_datetime(df_fit["ds"])

    df_recent = df_fit.sort_values("ds").tail(90)
    media_hist = df_recent["y"].mean()

    if pd.isna(media_hist) or media_hist <= 0:
        media_hist = df_fit["y"].mean()

    if pd.isna(media_hist) or media_hist <= 0:
        media_hist = 0

    if len(df_fit) < MIN_PONTOS_HISTORICO or media_hist == 0:
        datas_futuras = pd.date_range(
            df_fit["ds"].max() + pd.Timedelta(days=1),
            periods=DIAS_FORECAST,
            freq="D",
        )
        return pd.DataFrame({
            "Data": datas_futuras.date,
            "QtdPrevistaDia": [media_hist] * DIAS_FORECAST,
        })

    limite_outlier = media_hist * 6
    df_fit["y"] = df_fit["y"].clip(upper=limite_outlier)

    model = Prophet(
        yearly_seasonality=True,
        weekly_seasonality=False,
        daily_seasonality=False,
    )
    model.fit(df_fit)

    future = model.make_future_dataframe(periods=DIAS_FORECAST, freq="D")
    forecast = model.predict(future)

    forecast_future = forecast[forecast["ds"] > df_fit["ds"].max()].copy()
    forecast_future = forecast_future[["ds", "yhat"]].rename(
        columns={"ds": "Data", "yhat": "QtdPrevistaDia"}
    )

    max_aceitavel = media_hist * 3
    forecast_future["QtdPrevistaDia"] = forecast_future["QtdPrevistaDia"].clip(
        lower=0,
        upper=max_aceitavel,
    )

    forecast_future["Data"] = forecast_future["Data"].dt.date
    return forecast_future


# ==============================
# MAIN
# ==============================

def main():
    emitir_progresso(1, "Conectando ao HANA...")
    print("Conectando ao HANA...")
    conn = pyodbc.connect(HANA_CONN_STR)

    try:
        emitir_progresso(5, "Carregando historico de vendas...")
        print("Carregando historico de vendas...")
        df_hist = obter_historico_vendas(conn)
        if df_hist.empty:
            emitir_progresso(100, "Nenhum dado de historico encontrado.")
            print("Nenhum dado de historico encontrado.")
            return

        print(f"Total de linhas no historico: {len(df_hist)}")
        emitir_progresso(10, "Limpando previsoes antigas...")
        print("Limpando previsoes antigas...")
        limpar_previsoes_antigas(conn)

        todas_previsoes = []
        grupos = list(df_hist.groupby("ItemCode"))
        total_itens = len(grupos)

        for indice, (item_code, df_item) in enumerate(grupos, start=1):
            percentual = 10 + ((indice - 1) / max(total_itens, 1)) * 75
            emitir_progresso(percentual, f"Gerando previsao {indice}/{total_itens} - item {item_code}")
            print(f"Gerando previsao para item {item_code}...")
            df_prev_item = prever_por_item(df_item[["Data", "QtdVendida"]])
            df_prev_item.insert(0, "ItemCode", item_code)

            todas_previsoes.append(df_prev_item)

        if todas_previsoes:
            df_final = pd.concat(todas_previsoes, ignore_index=True)
            df_final["GeradoEm"] = datetime.now()
            emitir_progresso(88, "Salvando previsoes no HANA...")
            print(f"Salvando {len(df_final)} linhas de previsao no HANA...")
            salvar_previsoes(conn, df_final)
        else:
            print("Nenhuma previsao gerada.")

        emitir_progresso(100, "Atualizacao concluida.")
        print("Concluido.")
    finally:
        conn.close()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        emitir_progresso(100, f"Erro: {exc}")
        traceback.print_exc(file=sys.stderr)
        sys.exit(1)
