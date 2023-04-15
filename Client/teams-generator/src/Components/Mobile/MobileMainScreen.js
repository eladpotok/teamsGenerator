import { Button, Col, Divider, Drawer, Dropdown, Modal, Popconfirm, Row, Select, Space } from "antd";
import Config from "../Configuration/Config";
import { useContext, useEffect, useState } from "react";
import { DownOutlined, ExportOutlined, FileTextOutlined, OneToOneOutlined, SendOutlined, SettingOutlined, UploadOutlined } from "@ant-design/icons";
import { ConfigurationStoreContext } from "../../Store/ConfigurationStoreContext";
import { getInitialConfig } from "../../Adapters/DB/WebApiAdapter";
import { ConfigurationContext } from "../../Store/ConfigurationContext";
import MobilePlayersMenu from "./Players/MobilePlayersMenu";
import './MobileMainScreen.css'
import Teams from "../Teams/Teams";
import MobileHeader from "./MobileHeader";

function MobileMainScreen(props) {

    const configContext = useContext(ConfigurationContext);

    const [configDrawerOpen, setConfigDrawerOpen] = useState(false)
    const [teamsModalOpened, setTeamsModalOpened] = useState(false)

    function openCloseConfigDrawerHandler(isOpen) {
        setConfigDrawerOpen(isOpen)
    }

    function algoChangedHandler(value) {
        props.onAlgoChanged(value)
    };

    const algosItems = props.storeConfig.algos.map(algo => {
        return {
            label: algo.displayName,
            value: algo.algoKey.toString()
        }
    })

    return (

        <div style={{ display: 'flex', flexDirection: 'column' 
   
   
   ,
        alignItems: 'stretch',
        }}>
            <div style={{ margin: '4px' , flexGrow: '1'}}><MobileHeader storeConfig={props.storeConfig} onAlgoChanged={algoChangedHandler} defaultAlgoName={configContext.userConfig.algo.displayName} /></div>

            <div style={{ margin: '4px' , flexGrow: '2'}}>
                {configContext.userConfig.algo && <MobilePlayersMenu style={{ height: '10px' }} currentAlgo={configContext.userConfig.algo} />}
            </div>

            <div style={{
                backgroundColor: 'white',  flexGrow: '1'
               
            }}>
                <Row  >
                    <Col span={7}>
                        <Popconfirm onConfirm={props.onResetClicked} title='Reset all' description='Are you sure you want to clear all'>
                            <div style={{ verticalAlign: 'middle', textAlign: 'center', width: '100%', height: '65px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                <div style={{ display: 'flex', 'flex-direction': 'column', color: '#A6AAB3' }}>
                                    <FileTextOutlined style={{ fontSize: '28px', marginBottom: '4px' }} />
                                    <label style={{ fontSize: '12px' }}>Clear All</label>

                                </div>
                            </div>
                        </Popconfirm>
                    </Col>

                    <Col span={1}>

                        <Divider style={{ backgroundColor: "grey", height: '100%' }} type="vertical" />

                    </Col>


                    <Col span={7} >
                        {props.teams && <div style={{ verticalAlign: 'middle', textAlign: 'center', width: '100%', height: '65px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                            <div style={{ display: 'flex', 'flex-direction': 'column', color: '#A6AAB3' }}>
                                <OneToOneOutlined style={{ fontSize: '28px', marginBottom: '4px' }} />
                                <label style={{ fontSize: '12px' }}>Show Result</label>
                            </div>
                        </div>}
                    </Col>

                    <Col span={1}>

                        <Divider style={{ backgroundColor: "grey", height: '100%' }} type="vertical" />

                    </Col>


                    <Col span={8} >
                        <div onClick={async () => {
                            const succeeded = await props.onGenerateTeams()
                            if (succeeded) setTeamsModalOpened(true)
                        }} style={{ textAlign: 'center', backgroundColor: 'white', width: '100%', height: '100%' }}><div style={{ verticalAlign: 'middle', textAlign: 'center', width: '100%', height: '65px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                <div style={{ display: 'flex', 'flex-direction': 'column', color: '#A6AAB3' }}>
                                    <SendOutlined style={{ fontSize: '28px', marginBottom: '4px' }} />
                                    <label style={{ fontSize: '12px' }}>Generate</label>

                                </div>
                            </div></div>
                    </Col>
                </Row>
            </div>
            <Drawer title="Configuration" style={{ backgroundColor: '#4b525e' }}
                width='85%'
                onClose={() => { openCloseConfigDrawerHandler(false) }}
                open={configDrawerOpen}>
                {props.storeConfig && <Config backgroundColor='#4b525e' currentAlgo={props.storeConfig.algos.filter(t => t.algoKey == 0)[0]} shirtsColors={props.storeConfig.config.shirtsColors} numberOfTeams={props.storeConfig.config.numberOfTeams} />}
            </Drawer>

            <Modal onCancel={() => { setTeamsModalOpened(false) }} width={1000} open={teamsModalOpened} footer={[]}>
                {props.teams && <Teams teams={props.teams} />}
            </Modal>
        </div>

    )

}


export default MobileMainScreen;