﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Animations;
using MaterialSkin.Controls;
using System.Net;
using System.IO;
using System.Threading;
using System.Net.Sockets;
using System.Collections;

namespace SterCore
{
    public partial class OknoServeru : MaterialForm
    {
        public OknoServeru()
        {
            InitializeComponent();

            var materialSkinManager = MaterialSkinManager.Instance;
            materialSkinManager.AddFormToManage(this);
            materialSkinManager.Theme = MaterialSkinManager.Themes.LIGHT;
            materialSkinManager.ColorScheme = new ColorScheme(Primary.BlueGrey800, Primary.BlueGrey900, Primary.BlueGrey500, Accent.LightBlue200, TextShade.WHITE);
        } //Inicializuje okno a nastaví jeho vzhled

        public OknoServeru(IPAddress Adresa, int Port, int Pocet)
        {
            InitializeComponent();

            AdresaServeru = Adresa;
            PortServeru = Port;
            PocetKlientu = Pocet;

            var materialSkinManager = MaterialSkinManager.Instance;
            materialSkinManager.AddFormToManage(this);
            materialSkinManager.Theme = MaterialSkinManager.Themes.LIGHT;
            materialSkinManager.ColorScheme = new ColorScheme(Primary.BlueGrey800, Primary.BlueGrey900, Primary.BlueGrey500, Accent.LightBlue200, TextShade.WHITE);
            Shown += new EventHandler(OknoServeru_Shown);
        }

        public static Hashtable SeznamKlientu = new Hashtable();//Seznam připojených uživatelů a jejich adres
        public static IPAddress AdresaServeru;
        public static int PortServeru, PocetKlientu;//Proměná portu serveru a maximální počet klientů(0 znamená neomezený počet)
        public static int PocetPripojeni = 0;//Počet aktuálně připojených uživatelů
        bool Stop = false;//Proměná pro zastavení běhu serveru
        TcpListener PrichoziKomunikace;//Poslouchá příchozí komunikaci a žádosti i připojení
        Thread BehServeru;//Thread pro běh serveru na pozadí nezávisle na hlavním okně

        /// <summary>
        /// Po načtení formuláře spustí server.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OknoServeru_Shown(Object sender, EventArgs e)
        {
            StartServeru();
        }

        /// <summary>
        /// Nastaví IP adresu a port. Spustí naslouchání serveru pro připojení ve vlastním vlákně.
        /// </summary>
        private void StartServeru()
        {
            Stop = false;//Povolí běh serveru
            PrichoziKomunikace = new TcpListener(AdresaServeru, PortServeru);//Nastaví poslouchání žádostí a komunikace na adresu a port

            BehServeru = new Thread(PrijmaniKlientu)
            {
                IsBackground = true
            };//Připraví thread a nastaví jej do pozadí

            BehServeru.Start();
        }

        /// <summary>
        /// Přijímá připojení klientů. Duplikátní jména jsou odpojena.
        /// </summary>
        private void PrijmaniKlientu()//Funkce pro přijímaní připojení
        {           
            try
            {
                PrichoziKomunikace.Start();//Spustí poslouchání
                Invoke((MethodInvoker)(() => VypisChatu.Items.Add("Server byl spuštěn.")));

                while (!Stop)
                {
                    TcpClient Klient = PrichoziKomunikace.AcceptTcpClient();//Přijme žádost o připojení
                    ++PocetPripojeni;
                    byte[] ByteJmeno = new byte[1024 * 1024 * 2];//Bytové pole pro načtení jména
                    NetworkStream CteniJmena = Klient.GetStream();//Připojení načítání na správný socket
                    CteniJmena.Read(ByteJmeno, 0, Klient.ReceiveBufferSize);//Načtení sériových dat
                    string Jmeno = Encoding.UTF8.GetString(ByteJmeno).TrimEnd('\0');//Dekódování dat a vymazání prázdných znaků

                    if (KontrolaJmena(Jmeno))//Kontrola duplikátního jména
                    {
                        SeznamKlientu.Add(Jmeno, Klient);//Přidá klienta do seznamu
                        Invoke((MethodInvoker)(() => VypisKlientu.Items.Add(Jmeno)));//Vypíše klienta do seznamu na serveru
                        Vysilani("SERVER", Jmeno + " se připojil(a)"); //Oznámí všem klientům, že se připojil někdo připojil

                        Thread VlaknoKlienta = new Thread(() => ObsluhaKlienta(Jmeno, Klient))
                        {
                            IsBackground = true
                        };//Připraví thread pro obsluhu klienta a nastaví jej do pozadí

                        VlaknoKlienta.Start();
                    }
                    else
                    {
                        Vysilani("SERVER", Jmeno + " se pokusil(a) připojit. Pokus byl zamítnut - duplikátní jméno");
                        byte[] Oznameni = Encoding.UTF8.GetBytes("Pokus o připojení byl zamítnut - uživatel se stejným jménem je již připojen!");
                        CteniJmena.Write(Oznameni, 0, Oznameni.Length);
                        CteniJmena.Flush();
                        Klient.Close();
                        --PocetPripojeni;
                    }               
                }

            }
            catch(Exception x)//Kontrola chyb
            {
                Invoke((MethodInvoker)(() => VypisChatu.Items.Add("Objevila se chyba:")));
                Invoke((MethodInvoker)(() => VypisChatu.Items.Add(x.Message)));//Vypíše chybu na server
            }      
            finally
            {
                PrichoziKomunikace.Stop();//Konec naslouchání
                BehServeru.Join();
            }
        }

        /// <summary>
        /// Naslouchá příchozím zprávám od klienta.
        /// </summary>
        /// <param name="jmeno">Jméno klienta</param>
        /// <param name="Pripojeni">Připojení klienta</param>
        private void ObsluhaKlienta(string jmeno, TcpClient Pripojeni)//Naslouchá příchozím zprávám od klienta
        {
            using (NetworkStream Cteni = Pripojeni.GetStream())//Nastaví naslouchání na správnou adresu
            {
                byte[] HrubaData;//Pole pro přijímání zpráv
                string Zprava;

                try
                {
                    while (!Stop)
                    {
                        HrubaData = new byte[1024 * 1024 * 2];
                        Cteni.Read(HrubaData, 0, Pripojeni.ReceiveBufferSize);//Načtení sériových dat
                        Zprava = Encoding.UTF8.GetString(HrubaData).TrimEnd('\0');//Dekódování a vymazání prázdných znaků
                        string[] Uprava = Zprava.Split('φ');

                        switch (Uprava[0])
                        {
                            case "0"://Běžná zpráva
                                {
                                    Vysilani(jmeno, Uprava[1]);//Vyslání zprávy všem klientům
                                    break;
                                }
                            case "1"://TODO: Obrázek
                                {
                                    break;
                                }
                            case "2"://TODO: Soubor
                                {
                                    break;
                                }
                            case "3"://TODO: Obsluha
                                {
                                    break;
                                }
                            case "4"://TODO: Odpojení
                                {
                                    Exception x = new EndOfStreamException();
                                    break;
                                }
                        }
                    }
                }
                catch//Při chybě je klient odpojen
                {
                    OdebratKlienta(jmeno);
                }
            }                
        }

        /// <summary>
        /// Odešle zprávu všem připojeným klientům.
        /// </summary>
        /// <param name="Tvurce">Jméno odesílatele</param>
        /// <param name="Text">Obsah zprávy</param>
        private void Vysilani(string Tvurce, string Text)//Odeslání zprávy všem klientům
        {
            try
            {
                Invoke((MethodInvoker)(() => VypisChatu.Items.Add(Tvurce + ": " + Text)));//Vypíše zprávu na serveru
                Text = ("0φ" + Tvurce + "φ: " + Text);//Naformátuje zprávu před odesláním                
                byte[] Data = Encoding.UTF8.GetBytes(Text);//Převede zprávu na byty

                foreach (DictionaryEntry Klient in SeznamKlientu)
                {
                    TcpClient VysilaniSocket = (TcpClient)Klient.Value;//Nastavení adresy k odeslání
                    NetworkStream VysilaniProud = VysilaniSocket.GetStream();//Nastaví odesílací stream na adresu                        
                    VysilaniProud.Write(Data, 0, Data.Length);//Odeslání sériových dat
                    VysilaniProud.Flush();//Ukončení odesílání
                }
            }
            catch(Exception x)
            {
                Invoke((MethodInvoker)(() => VypisKlientu.Items.Add("Objevila se chyba:")));
                Invoke((MethodInvoker)(() => VypisKlientu.Items.Add(x.Message)));//Vypíše chybu na server
            }
        }

        /// <summary>
        /// Ukončí všechna připojení a vypne server.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnServerStop_Click(object sender, EventArgs e)//Ukončení běhu serveru
        {
            foreach(DictionaryEntry Klient in SeznamKlientu)
            {
                (Klient.Value as TcpClient).Close();
            }

            UvodServeru.ZmenaUdaju = true;
            Stop = true;

            Close();
        }

        /// <summary>
        /// Zkontroluje, jestli není připojený uživatel se stejným jménem.
        /// </summary>
        /// <param name="Jmeno">Jméno ke kontrole</param>
        /// <returns>Jméno je v pořádku</returns>
        private bool KontrolaJmena(string Jmeno)//Zkontroluje, zda se jméno již nevyskytuje
        {
            foreach(DictionaryEntry Klient in SeznamKlientu)
            {
                if ((string)Klient.Key == Jmeno)
                {
                    return false;//V seznamu se již jméno nachází
                }
            }

            return true;//V seznamu se nenachází
        }

        /// <summary>
        /// Odpojí klienta ze serveru.
        /// </summary>
        /// <param name="Jmeno">Jméno klienta</param>
        private void OdebratKlienta(string Jmeno)
        {
            --PocetPripojeni;

            if (InvokeRequired)//Odstraní klienta z výpisu
            {
                Invoke((MethodInvoker)(() => VypisKlientu.Items.Remove(Jmeno)));
            }
            else
            {
                VypisKlientu.Items.Remove(Jmeno);
            }

            (SeznamKlientu[Jmeno] as TcpClient).Close();
            Invoke((MethodInvoker)(() => SeznamKlientu.Remove(Jmeno)));//Odstraní klienta ze seznamu
            VypisKlientu.Items.Remove(Jmeno);
            Vysilani("SERVER", Jmeno + " se odpojil(a)");//Ohlasí odpojení ostatním klientům
        }

        /// <summary>
        /// Odešle zprávu ze serveru.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnZprava_Click(object sender, EventArgs e)
        {
            if(!string.IsNullOrWhiteSpace(TxtZprava.Text))
            {
                Vysilani("Server", TxtZprava.Text);
                TxtZprava.Text = null;
                TxtZprava.Focus();
                TxtZprava.SelectAll();
            }           
        }

        /// <summary>
        /// Odeslání zprávy pomocí enteru.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TxtZprava_KeyPress(object sender, KeyPressEventArgs e)
        {
            if(e.KeyChar == (char)Keys.Enter)
            {
                BtnZprava_Click(null, null);
            }
        }
    }
}
