import paho.mqtt.client as client
import struct
import pigpio
import json
from nrf24 import *
import threading
import sys

TOPIC_DIREZIONE = "tps/motore/direzione"
TOPIC_VELOCITA = "tps/motore/velocita"
BROKER = sys.argv[1:][0]
nuova_velocità = False
nuova_direzione = False
v = 0
d = "sinistra"

ID=b"BL"
MITTENTE=b"PY01"
DESTINATARIO=b"ARD1"
TIPO=b"A1"
VUOTO=b"----------------"

PIGPIONAME='localhost'
PIGPIOPORT=8888
READINGPIPE='00001'


pi = pigpio.pi(PIGPIONAME, PIGPIOPORT)
if not pi.connected:
    print("Pigpiod non connesso. Lanciare: SUDO PIGPIOD")
    sys.exit()
 
nrf = NRF24(pi, ce=17, payload_size=32, channel=76, data_rate=RF24_DATA_RATE.RATE_1MBPS, pa_level=RF24_PA.LOW)

nrf.set_address_bytes(5)
nrf.open_writing_pipe(READINGPIPE)

def on_connect_direzione(subscriber, userdata, flags, rc):
    print(f"Connesso con return code {str(rc)} al topic direzione")
    subscriber.subscribe(TOPIC_DIREZIONE)

def on_message_direzione(subscriber, userdata, msg):
    dato = msg.payload.decode()
    print(f"{msg.topic}: {dato}")
    global d
    d = json.loads(dato)["direzione"]
    global nuova_direzione
    nuova_direzione = True

def on_connect_velocità(subscriber, userdata, flags, rc):
    print(f"Connesso con return code {str(rc)} al topic velocità")
    subscriber.subscribe(TOPIC_VELOCITA)

def on_message_velocità(subscriber, userdata, msg):
    dato = msg.payload.decode()
    print(f"{msg.topic}: {dato}")
    global v
    v = json.loads(dato)["velocita"]
    global nuova_velocità
    nuova_velocità = True

subscriber_direzione = client.Client()
subscriber_direzione.on_connect = on_connect_direzione
subscriber_direzione.on_message = on_message_direzione

subscriber_direzione.connect(BROKER, 1883)

def dir_loop():
    subscriber_direzione.loop_forever()

dir_loop_t = threading.Thread(target=dir_loop)
dir_loop_t.setDaemon(True)

subscriber_velocità = client.Client()
subscriber_velocità.on_connect = on_connect_velocità
subscriber_velocità.on_message = on_message_velocità

subscriber_velocità.connect(BROKER, 1883)

def vel_loop():
    subscriber_velocità.loop_forever()

vel_loop_t = threading.Thread(target=vel_loop)
vel_loop_t.setDaemon(True)

dir_loop_t.start()
vel_loop_t.start()

while True:
    if nuova_direzione and nuova_velocità:
        venc = str(v).zfill(3).encode()
        dir = b"a" if d == "destra" else b"i"
        packet = struct.pack("2s 4s 4s 2s 1s 3s 16s", ID,
            MITTENTE, DESTINATARIO, TIPO, dir, venc, VUOTO)
        nrf.send(packet)
        print(packet)
        nuova_direzione = False
        nuova_velocità = False