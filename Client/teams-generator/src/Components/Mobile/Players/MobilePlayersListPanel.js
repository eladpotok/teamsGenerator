import { CheckSquareOutlined, ClearOutlined, DownOutlined, ExportOutlined, FileDoneOutlined, MoreOutlined, NumberOutlined, SettingOutlined, UploadOutlined, UserAddOutlined } from "@ant-design/icons";
import { Button, Col, Drawer, Dropdown, Image, Modal, Row, Space, message } from "antd";
import { useContext, useState } from "react";
import ImportPlayer from "../../Common/ImportPlayer";
import { writeFileHandler } from "../../../Utilities/Helpers";
import AddPlayerForm from "../../Common/AddPlayerForm";
import { AnalyticsContext } from "../../../Store/AnalyticsContext";
import { UserContext } from "../../../Store/UserContext";
import { PlayersContext } from "../../../Store/PlayersContext";
import { GiSoccerKick } from 'react-icons/gi';
import { MdOutlineCancelPresentation } from "react-icons/md";
import { VscPersonAdd } from "react-icons/vsc";
import AppButton from "../../Common/AppButton";
import MyIconButton from "../../Common/MyIconButton";
import { RxHamburgerMenu } from "react-icons/rx";
import { RiUserAddFill } from "react-icons/ri";
import { CiSettings } from "react-icons/ci";
import Config from "../../Configuration/Config";
import { ConfigurationContext } from "../../../Store/ConfigurationContext";

function MobilePlayersListPanel(props) {
    const [addPlayerDrawerOpen, setAddPlayerDrawerOpen] = useState(false)
    const analyticsContext = useContext(AnalyticsContext)
    const userContext = useContext(UserContext)
    const playersContext = useContext(PlayersContext)
    const configContext = useContext(ConfigurationContext);

    const [playerToAdd, setPlayerToAdd] = useState({})
    const [messageApi, contextHolder] = message.useMessage();
    const [curConfig, setinitiatedConfig] = useState(null)
    const [configDrawerOpen, setConfigDrawerOpen] = useState(false)


    const items = [
        {
            label: <div  onClick={()=>{props.onMarkAllPlayers(true)}}>Mark All</div>,
            key: '1',
            icon: <FileDoneOutlined />
        },
        {
            label: <div onClick={()=>{props.onMarkAllPlayers(false)}}>Unmark All</div>,
            key: '2',
            icon: <FileDoneOutlined />
        },
        {
            label: <ImportPlayer algos={props.algos}>
                Import
            </ImportPlayer>,
            key: '3',
            icon: <UploadOutlined />,
        },
        {
            label: <div onClick={() => { writeFileHandler(props.players, props.currentAlgo); analyticsContext.sendAnalyticsEngagement(userContext.user.uid, 'exportPlayers', null)
        }}>Export</div>,
            key: '4',
            icon: <ExportOutlined />,
        },
        {
            label: <div onClick={props.onClearPlayers}>Clear Players</div>,
            key: '5',
            icon: <ClearOutlined/>,
        }
    ];

    function openAddPlayerViewHandler() {
        setPlayerToAdd(getDefaultValues(props.currentAlgo.playerProperties));
        setAddPlayerDrawerOpen(true)
    }

    function resetClickedHandler() {
        setPlayerToAdd(getDefaultValues(props.currentAlgo.playerProperties));
    }

    function playerSubmittedHandler() {
        analyticsContext.sendAnalyticsEngagement(userContext.user.uid, 'addPlayer', playerToAdd)
        playersContext.setPlayers([...playersContext.players, playerToAdd])
        setPlayerToAdd(getDefaultValues(props.currentAlgo.playerProperties));

        messageApi.open({
            type: 'success',
            content: 'Player Added',
          });
    }

    function openConfigView(){
        const userConfigContext = {...configContext.userConfig}
        setinitiatedConfig(userConfigContext)
        openCloseConfigDrawerHandler(true)
    }

    function openCloseConfigDrawerHandler(isOpen) {
        setConfigDrawerOpen(isOpen)
    }

    return (
        <Row style={{ background: 'rgba(255, 255, 255, 0.5)' , borderBottomRightRadius: '15px', borderBottomLeftRadius: '15px' }}>
            {contextHolder}
            <Col flex='auto'>
                <div style={{ display: 'flex', color: 'black', margin: '10px' }}>
                    <NumberOutlined style={{margin: '3px'}} size='small' />Total: {props.players.length} 
                    <CheckSquareOutlined size='small' style={{marginLeft: '13px', marginTop:'3px', marginRight: '3px'}} /> Arrive: {props.arrivedPlayers.length}</div>
            </Col>
            <Col style={{display: 'flex'}} >
                <div style={{marginRight: '4px'}}><MyIconButton buttonType='empty' icon={<RiUserAddFill style={{fontSize: '18px'}}/>}  onClick={openAddPlayerViewHandler}  /></div>
                <div style={{marginRight: '4px'}}>
                    <MyIconButton onClick={openConfigView} buttonType='empty' icon={<SettingOutlined style={{ fontSize: '18px'}} />}/>
                </div>
                <div style={{marginRight: '4px'}}>
                    <Dropdown menu={{ items }} placement="bottomLeft" arrow trigger={['click']}>
                        <MyIconButton buttonType='empty' icon={<RxHamburgerMenu style={{ fontSize: '18px'}} />}/>
                    </Dropdown>
                </div>
            </Col>

            {props.currentAlgo && <Modal title={<><GiSoccerKick style={{marginBottom: '-2px', marginRight: '4px',  color: '#095c1f', marginLeft: '4px'}} /><label style={{marginLeft: '4px', color: '#095c1f'}}>NEW PLAYER</label></>}  style={{top: 20}} 
                        footer={[]} 
                        closable={false}
                        open={addPlayerDrawerOpen}>
                    <AddPlayerForm player={playerToAdd} playersProperties={props.currentAlgo.playerProperties} onCancelClicked={()=>{setAddPlayerDrawerOpen(false)}} onResetClicked={resetClickedHandler} onPlayerSubmitted={playerSubmittedHandler}/>
                </Modal>}

            <Modal title={<><CiSettings style={{fontSize:'22px', marginBottom: '-5px', color: '#095c1f'}} /><label style={{marginLeft: '4px', color: '#095c1f'}}>CONFIGURATION</label></>} 
               footer={[]} 
               closable={false}
               open={configDrawerOpen}>
               {props.storeConfig && <Config onCancelClicked={()=>{openCloseConfigDrawerHandler(false)}} onSubmitClicked={()=>{openCloseConfigDrawerHandler(false)}} currentAlgo={props.storeConfig.algos.filter(t => t.algoKey === 0)[0]} initiatedConfig={curConfig} optionalShirts={props.storeConfig.config.shirtsColors}/>}
            </Modal>
        </Row>

        
    )
}

function getDefaultValues(properties) {
    let playerDefaultValues = {}
    properties.forEach(element => {
        if (element.type == 'number') {
            playerDefaultValues[element.name] = 4
        }
        if (element.type == 'text') {
            console.log('test', element.name, playerDefaultValues)
            playerDefaultValues[element.name] = ""
        }

    });

    return playerDefaultValues
}

export default MobilePlayersListPanel;